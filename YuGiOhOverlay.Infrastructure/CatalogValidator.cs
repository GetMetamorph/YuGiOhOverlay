using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public static class CatalogValidator
{
    public static void Validate(ComboCatalog catalog)
    {
        if (catalog.Version != 2 || string.IsNullOrWhiteSpace(catalog.DeckId)
            || string.IsNullOrWhiteSpace(catalog.DeckName) || catalog.Cards is null || catalog.Rules is null
            || catalog.ImportWarnings is null || catalog.Cards.Count > 1000 || catalog.Rules.Count > 500)
            throw new InvalidDataException("Catalogue invalide ou version non prise en charge (version 2 attendue).");
        if (catalog.Cards.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name))
            || catalog.Cards.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Cards.Count)
            throw new InvalidDataException("Identifiants de cartes invalides ou dupliqués.");
        var ids = catalog.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var rules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in catalog.Rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || !rules.Add(rule.Id)
                || string.IsNullOrWhiteSpace(rule.Title) || rule.Conditions?.Alternatives is null
                || rule.Conditions.Alternatives.Count is < 1 or > 100 || rule.Actions is null
                || rule.Actions.Count is < 1 or > 200 || rule.Actions.Any(string.IsNullOrWhiteSpace)
                || rule.RequiredCards is null || rule.RequiredCards.Any(string.IsNullOrWhiteSpace)
                || rule.ExcludedCardIds is null || rule.ExcludedCardIds.Any(id => !ids.Contains(id))
                || !IsSourceUrl(rule.SourceUrl) || rule.Explanation is null || rule.Author is null)
                throw new InvalidDataException("Règle incomplète, dupliquée ou source invalide.");
            foreach (var alternative in rule.Conditions.Alternatives)
            {
                if (alternative is null || alternative.Count is < 1 or > 20
                    || alternative.Any(r => r is null || !ids.Contains(r.CardId) || r.Count is < 1 or > 3)
                    || alternative.Select(r => r.CardId).Distinct(StringComparer.Ordinal).Count() != alternative.Count)
                    throw new InvalidDataException("Condition de départ invalide ou ambiguë.");
            }
        }
    }

    public static bool IsSourceUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "www.masterduelmeta.com"
        && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.AbsolutePath.StartsWith("/combo/", StringComparison.Ordinal)
            || uri.AbsolutePath.TrimEnd('/') == "/combos-and-counters");
}
