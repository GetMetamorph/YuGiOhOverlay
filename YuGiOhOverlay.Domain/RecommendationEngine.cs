namespace YuGiOhOverlay.Domain;

public interface IRecommendationEngine
{
    IReadOnlyList<RecommendedAction> Recommend(ComboCatalog catalog, BoardState state, DateTimeOffset now);
}

public sealed class RecommendationEngine(double minimumConfidence, TimeSpan maximumAge) : IRecommendationEngine
{
    public IReadOnlyList<RecommendedAction> Recommend(ComboCatalog catalog, BoardState state, DateTimeOffset now)
    {
        if (!state.IsStable || state.ObservedAt > now || now - state.ObservedAt > maximumAge)
            return [];

        var hand = state.Hand.Where(d => d.CardId is not null && double.IsFinite(d.Confidence)
            && d.Confidence >= minimumConfidence && d.Confidence <= 1 && d.Zone.IsValid)
            .GroupBy(d => d.CardId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.Confidence).ToArray(), StringComparer.Ordinal);
        var results = new List<RecommendedAction>();
        foreach (var rule in catalog.Rules)
        {
            if (rule.ExcludedCardIds.Any(hand.ContainsKey)) continue;
            var scores = rule.Conditions.Alternatives.Where(a => a.Count > 0)
                .Where(a => a.All(r => hand.TryGetValue(r.CardId, out var copies) && copies.Length >= r.Count))
                .Select(a => a.Min(r => hand[r.CardId].Take(r.Count).Min(d => d.Confidence))).ToArray();
            if (scores.Length == 0) continue;
            results.Add(new(rule, scores.Max(),
                "Cartes de départ reconnues. Vérifier le terrain, les ressources et les cartes requises avant de suivre ce guide."));
        }

        return results.OrderByDescending(r => r.Combo.Priority)
            .ThenBy(r => r.Combo.Id, StringComparer.Ordinal).ToArray();
    }
}
