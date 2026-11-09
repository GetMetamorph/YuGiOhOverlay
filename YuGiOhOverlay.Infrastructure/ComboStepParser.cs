using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

// Only verified MDM symbol names are interpreted. Free-form author text remains
// visible; it is never executed or treated as a proof that an effect resolved.
internal static class ComboStepParser
{
    public static IReadOnlyList<ComboStep> Parse(JsonElement steps, int depth = 0)
    {
        if (depth > 5) throw new NotSupportedException("branches trop imbriquées");
        return steps.EnumerateArray().Select(step =>
        {
            var entries = step.GetProperty("entries").EnumerateArray().ToArray();
            var instructions = entries.Select(Instruction).ToArray();
            var condition = step.TryGetProperty("condition", out var value) ? value : default;
            var branches = condition.ValueKind == JsonValueKind.Object && condition.TryGetProperty("branches", out var array)
                ? array.EnumerateArray().Select(b => new ComboBranch(Text(b, "label"), Parse(b.GetProperty("steps"), depth + 1))).ToArray() : [];
            var label = Text(condition, "label");
            return new ComboStep(instructions, label, branches, branches.Length == 0 && label.Length == 0 ? Cue(entries) : null);
        }).ToArray();
    }

    public static ComboStep Remap(ComboStep step, Func<string, string> canonical) => step with
    {
        Instructions = step.Instructions.Select(i => i with { CardId = i.CardId is null ? null : canonical(i.CardId) }).ToArray(),
        Branches = step.Branches.Select(b => b with { Steps = b.Steps.Select(s => Remap(s, canonical)).ToArray() }).ToArray(),
        Cue = step.Cue is null ? null : step.Cue with
        {
            Cards = step.Cue.Cards.GroupBy(c => canonical(c.CardId), StringComparer.Ordinal)
                .Select(g => new CardRequirement(g.Key, g.Sum(c => c.Count))).ToArray()
        }
    };

    private static ComboInstruction Instruction(JsonElement entry)
    {
        var card = entry.TryGetProperty("card", out var value) ? value : default;
        var name = Text(card, "name");
        if (name.Length == 0) name = Text(entry, "generic");
        var symbol = Text(entry, "symbol");
        var action = symbol switch
        {
            "summon" => "Invoquer",
            "search" => "Ajouter à la main",
            "effect" => "Activer l’effet",
            "move" => "Déplacer / utiliser",
            "chain" => "Chaîner",
            "graveyard" => "Cimetière",
            "set_st" => "Poser Magie / Piège",
            "set" => "Poser",
            "hand" => "Main",
            "banish" => "Bannir",
            "" => "Action",
            _ => symbol
        };
        var detail = Text(entry, "label");
        if (entry.TryGetProperty("overlayCard", out var extra)) detail += " · " + Text(extra, "name");
        if (entry.TryGetProperty("paren", out var paren)) name = paren.GetString() == "open" ? "(" : ")";
        return new(Text(card, "_id") is { Length: > 0 } id ? id : null, name, action, detail.Trim(' ', '·'), Text(entry, "connector"));
    }

    private static VisualStepCue? Cue(JsonElement[] entries)
    {
        if (entries.Length == 0) return null;
        // Observe only an unambiguous terminal outcome: one or more cards added to
        // the hand, or summoned to the allied field. Never infer effect resolution.
        if (entries.Any(e => Text(e, "connector") == "or" || e.TryGetProperty("paren", out _)
            || Regex.IsMatch(Text(e, "label"), @"\b(if|unless|when|si|sinon)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))) return null;
        var last = entries[^1];
        if (Text(last, "connector").Length != 0) return null;
        var symbol = Text(last, "symbol");
        if (symbol is not ("summon" or "search")) return null;
        var tail = new List<JsonElement> { last };
        for (int i = entries.Length - 2; i >= 0 && Text(entries[i], "connector") == "plus"; i--) tail.Insert(0, entries[i]);
        if (tail.Any(e => Text(e, "symbol") != symbol || !e.TryGetProperty("card", out _))) return null;
        // An "activate on summon" annotation includes an unresolved effect.
        var allowedSummonLabels = new[] { "", "normal summon", "special summon", "summon from deck", "summon from ed",
            "summon from gy", "link summon", "synchro summon", "fusion summon", "xyz summon", "ritual summon" };
        var allowedSearchLabels = new[] { "", "add from deck", "to hand", "add to hand" };
        var allowed = symbol == "summon" ? allowedSummonLabels : allowedSearchLabels;
        if (tail.Any(e => !allowed.Contains(Text(e, "label").Trim().ToLowerInvariant()))) return null;
        var requirements = tail.Select(e => Text(e.GetProperty("card"), "_id")).GroupBy(id => id, StringComparer.Ordinal)
            .Select(g => new CardRequirement(g.Key, g.Count())).ToArray();
        return new(symbol == "summon" ? ObservationArea.AlliedField : ObservationArea.Hand, requirements);
    }

    private static string Text(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        ? WebUtility.HtmlDecode(value.GetString()!).Trim() : "";
}
