namespace YuGiOhOverlay.Domain;

public sealed record Card(string Id, string Name);

public sealed record CardRequirement(string CardId, int Count);

// Each inner list is an AND; alternatives are OR. Quantities consume distinct copies.
public sealed record Condition(IReadOnlyList<IReadOnlyList<CardRequirement>> Alternatives);

public sealed record ComboRule(
    string Id, string Title, int Priority, Condition Conditions,
    IReadOnlyList<string> Actions, string SourceUrl, string Author,
    string Explanation, IReadOnlyList<string> RequiredCards,
    IReadOnlyList<string> ExcludedCardIds);

public sealed record ComboCatalog(
    int Version, string DeckId, string DeckName, DateTimeOffset ImportedAt,
    IReadOnlyList<Card> Cards, IReadOnlyList<ComboRule> Rules,
    IReadOnlyList<string> ImportWarnings);

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y)
        && double.IsFinite(Width) && double.IsFinite(Height)
        && X >= 0 && Y >= 0 && Width > 0 && Height > 0
        && X + Width <= 1.000001 && Y + Height <= 1.000001;

    public double Overlap(NormalizedRect other)
    {
        var area = Math.Max(0, Math.Min(X + Width, other.X + other.Width) - Math.Max(X, other.X))
            * Math.Max(0, Math.Min(Y + Height, other.Y + other.Height) - Math.Max(Y, other.Y));
        return area / Math.Max(double.Epsilon, Math.Min(Width * Height, other.Width * other.Height));
    }
}

public sealed record CardDetection(string? CardId, double Confidence, NormalizedRect Zone);

public sealed record BoardState(DateTimeOffset ObservedAt, IReadOnlyList<CardDetection> Hand, bool IsStable);

public sealed record RecommendedAction(ComboRule Combo, double Confidence, string Explanation);
