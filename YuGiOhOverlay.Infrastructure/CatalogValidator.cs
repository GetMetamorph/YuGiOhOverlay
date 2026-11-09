using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public static class CatalogValidator
{
    public static void Validate(ComboCatalog catalog)
    {
        if (catalog.Version is not (2 or 3) || string.IsNullOrWhiteSpace(catalog.DeckId)
            || string.IsNullOrWhiteSpace(catalog.DeckName) || catalog.Cards is null || catalog.Rules is null
            || catalog.ImportWarnings is null || catalog.Cards.Count > 1000 || catalog.Rules.Count > 500)
            throw new InvalidDataException("Catalogue invalide ou version non prise en charge (versions 2 et 3 acceptées).");
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
            if (catalog.Version == 2 && rule.GuideSteps is not null)
                throw new InvalidDataException("Les étapes structurées nécessitent le format v3.");
            if (catalog.Version == 3)
            {
                if (rule.GuideSteps is null || rule.GuideSteps.Count != rule.Actions.Count)
                    throw new InvalidDataException("Les étapes du guide doivent correspondre aux actions.");
                foreach (var step in rule.GuideSteps) ValidateStep(step, ids, 0);
            }
        }
    }

    private static void ValidateStep(ComboStep step, HashSet<string> ids, int depth)
    {
        if (step is null || depth > 5 || step.Instructions is null || step.Instructions.Count > 100
            || step.Branches is null || step.Branches.Count > 20 || step.ConditionLabel is null
            || step.Instructions.Count == 0 && step.Branches.Count == 0
            || step.Instructions.Any(i => i is null || i.CardName is null || i.Detail is null
                || string.IsNullOrWhiteSpace(i.Action) || i.Connector is null || i.CardId is not null && !ids.Contains(i.CardId)))
            throw new InvalidDataException("Étape structurée invalide.");
        if (step.Cue is { } cue && (cue.Area is not (ObservationArea.Hand or ObservationArea.AlliedField)
            || step.Branches.Count != 0 || step.ConditionLabel.Length != 0 || cue.Cards is null || cue.Cards.Count is < 1 or > 10
            || cue.Cards.Any(r => r is null || !ids.Contains(r.CardId) || r.Count is < 1 or > 3
                || !step.Instructions.Any(i => i.CardId == r.CardId))
            || cue.Cards.Select(r => r.CardId).Distinct(StringComparer.Ordinal).Count() != cue.Cards.Count))
            throw new InvalidDataException("Indice visuel invalide ou ambigu.");
        foreach (var branch in step.Branches)
        {
            if (branch is null || string.IsNullOrWhiteSpace(branch.Label) || branch.Steps is null || branch.Steps.Count is < 1 or > 200)
                throw new InvalidDataException("Branche du guide invalide.");
            foreach (var child in branch.Steps) ValidateStep(child, ids, depth + 1);
        }
    }

    public static bool IsSourceUrl(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "www.masterduelmeta.com"
        && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.AbsolutePath.StartsWith("/combo/", StringComparison.Ordinal)
            || uri.AbsolutePath.TrimEnd('/') == "/combos-and-counters");
}
