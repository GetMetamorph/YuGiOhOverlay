using System.Text.Json;
using System.Text.RegularExpressions;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

// Adapter for the public response observed on 2026-11-09. Unknown starter expressions
// are excluded instead of weakening their conditions; display text is never executable.
public sealed class MasterDuelMetaParser
{
    public ComboCatalog Parse(IEnumerable<JsonElement> rows, string deckId, DateTimeOffset importedAt)
    {
        var cards = new Dictionary<string, Card>(StringComparer.Ordinal);
        var rules = new List<ComboRule>();
        var warnings = new List<string>();
        string? deckName = null;
        foreach (var row in rows)
        {
            var id = Required(row, "_id");
            var title = Required(row, "title");
            if (Required(row.GetProperty("deckType"), "_id") != deckId)
                throw new InvalidDataException("Le serveur a renvoyé un combo d'un autre deck.");
            deckName ??= Required(row.GetProperty("deckType"), "name");
            CollectCards(row, cards);
            try
            {
                var condition = ParseStarters(row.GetProperty("startingCards"));
                var actions = FormatSteps(row.GetProperty("steps"), 0).ToArray();
                var required = Entries(row, "requiredCards").Select(FormatEntry).ToArray();
                var excluded = Entries(row, "garnetCards")
                    .Select(c => c.TryGetProperty("card", out var wrapped) ? Required(wrapped, "_id") : Required(c, "_id")).ToArray();
                var votes = Entries(row, "upvotes").Count() - Entries(row, "downvotes").Count();
                rules.Add(new(id, title, votes, condition, actions,
                    $"https://www.masterduelmeta.com/combo/{id}",
                    row.TryGetProperty("author", out var author) ? Optional(author, "username") : "",
                    Optional(row, "notes"), required, excluded));
            }
            catch (NotSupportedException ex) { warnings.Add($"{title} : {ex.Message}"); }
        }
        // Different artworks can carry different MDM IDs for the same printed card.
        // Use a deterministic canonical ID within this import and preserve quantities.
        var canonical = cards.Values.GroupBy(c => c.Name, StringComparer.Ordinal)
            .Select(g => g.OrderBy(c => c.Id, StringComparer.Ordinal).First()).ToArray();
        var canonicalByName = canonical.ToDictionary(c => c.Name, c => c.Id, StringComparer.Ordinal);
        string CanonicalId(string id) => canonicalByName[cards[id].Name];
        rules = rules.Select(rule => rule with
        {
            Conditions = new(rule.Conditions.Alternatives.Select(alternative => (IReadOnlyList<CardRequirement>)alternative
                .GroupBy(r => CanonicalId(r.CardId), StringComparer.Ordinal)
                .Select(g => new CardRequirement(g.Key, g.Sum(r => r.Count))).ToArray()).ToArray()),
            ExcludedCardIds = rule.ExcludedCardIds.Select(CanonicalId).Distinct(StringComparer.Ordinal).ToArray()
        }).ToList();
        var catalog = new ComboCatalog(2, deckId, deckName ?? "Deck sans combo", importedAt,
            canonical.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray(), rules, warnings);
        CatalogValidator.Validate(catalog);
        return catalog;
    }

    private static Condition ParseStarters(JsonElement starters)
    {
        if (starters.ValueKind != JsonValueKind.Array || starters.GetArrayLength() == 0)
            throw new InvalidDataException("Cartes de départ absentes.");
        var entries = starters.EnumerateArray().ToArray();
        foreach (var entry in entries)
        {
            if (!entry.TryGetProperty("card", out _) || entry.TryGetProperty("paren", out _)
                || entry.TryGetProperty("generic", out _) || entry.TryGetProperty("group", out _)
                || entry.TryGetProperty("isGarnet", out var garnet) && garnet.ValueKind == JsonValueKind.True)
                throw new NotSupportedException("prérequis générique ou groupé à consulter sur le site");
            // A visual symbol such as search/summon is contextual, not proof of a hand card.
            if (Optional(entry, "symbol") is not ("" or "hand")
                || Optional(entry, "label").ToLowerInvariant() is not ("" or "in hand"))
                throw new NotSupportedException("condition de zone ou annotation à vérifier sur le site");
        }
        var connectors = entries.Take(entries.Length - 1).Select(e => Optional(e, "connector")).ToArray();
        if (connectors.Any(c => c is not ("" or "plus" or "or"))
            || connectors.Contains("or") && connectors.Any(c => c != "or"))
            throw new NotSupportedException("combinaison AND/OR ambiguë à consulter sur le site");
        var ids = entries.Select(e => Required(e.GetProperty("card"), "_id")).ToArray();
        IReadOnlyList<IReadOnlyList<CardRequirement>> alternatives = connectors.Contains("or")
            ? ids.Distinct(StringComparer.Ordinal).Select(id => (IReadOnlyList<CardRequirement>)new[] { new CardRequirement(id, 1) }).ToArray()
            : new[] { (IReadOnlyList<CardRequirement>)ids.GroupBy(id => id, StringComparer.Ordinal).Select(g => new CardRequirement(g.Key, g.Count())).ToArray() };
        return new(alternatives);
    }

    private static IEnumerable<string> FormatSteps(JsonElement steps, int depth)
    {
        if (depth > 5) throw new NotSupportedException("branches trop imbriquées");
        foreach (var step in steps.EnumerateArray())
        {
            var text = string.Join(" ", Entries(step, "entries").Select(e => FormatEntry(e) + Connector(Optional(e, "connector"))));
            var branches = step.TryGetProperty("condition", out var condition) ? Entries(condition, "branches").ToArray() : [];
            if (branches.Length > 0)
            {
                text += "\nChoix manuel — " + Optional(condition, "label");
                foreach (var branch in branches)
                    text += "\n• Si " + Required(branch, "label") + ":\n  "
                        + string.Join("\n  ", FormatSteps(branch.GetProperty("steps"), depth + 1));
            }
            if (string.IsNullOrWhiteSpace(text)) throw new NotSupportedException("étape vide ou inconnue");
            yield return text.Trim();
        }
    }

    private static string Connector(string connector) => connector switch
    {
        "" => "",
        "plus" => " +",
        "or" => " OU",
        "arrow" => " →",
        "equals" => " =",
        _ => " [" + connector + "]"
    };

    private static string FormatEntry(JsonElement entry)
    {
        var name = entry.TryGetProperty("card", out var card) ? Required(card, "name")
            : Optional(entry, "generic");
        if (entry.TryGetProperty("paren", out var paren)) return paren.GetString() == "open" ? "(" : ")";
        var label = Optional(entry, "label");
        var symbol = Optional(entry, "symbol");
        var overlay = entry.TryGetProperty("overlayCard", out var extra) && extra.ValueKind == JsonValueKind.Object
            ? " [" + Optional(extra, "name") + "]" : "";
        if (string.IsNullOrWhiteSpace(name + label)) throw new NotSupportedException("entrée sans libellé");
        return name + (symbol.Length > 0 ? $" [{symbol}]" : "") + overlay + (label.Length > 0 ? " — " + label : "");
    }

    private static void CollectCards(JsonElement node, Dictionary<string, Card> cards)
    {
        if (node.ValueKind == JsonValueKind.Array) { foreach (var child in node.EnumerateArray()) CollectCards(child, cards); }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name is "card" or "overlayCard" && property.Value.ValueKind == JsonValueKind.Object)
                {
                    var id = Required(property.Value, "_id");
                    if (!IsObjectId(id)) throw new InvalidDataException("Identifiant de carte invalide.");
                    cards[id] = new(id, Required(property.Value, "name"));
                }
                else if (property.Name == "garnetCards" && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var card in property.Value.EnumerateArray())
                        if (card.TryGetProperty("name", out _)) cards[Required(card, "_id")] = new(Required(card, "_id"), Required(card, "name"));
                    CollectCards(property.Value, cards);
                }
                else CollectCards(property.Value, cards);
            }
        }
    }

    public static bool IsObjectId(string id) => Regex.IsMatch(id, "^[a-f0-9]{24}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static IEnumerable<JsonElement> Entries(JsonElement node, string key) => node.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static string Optional(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static string Required(JsonElement node, string key) => Optional(node, key) is { Length: > 0 } value
        ? value : throw new InvalidDataException($"Champ Master Duel Meta manquant : {key}.");
}
