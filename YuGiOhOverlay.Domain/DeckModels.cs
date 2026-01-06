namespace YuGiOhOverlay.Domain;

public sealed record AppData(
    int Version,
    IReadOnlyList<DeckDefinition>? Decks);

public sealed record DeckDefinition(
    string DeckId,
    string Name,
    IReadOnlyList<CardPlan>? Cards);

public sealed record CardPlan(
    string CardId,
    string Name,
    IReadOnlyList<string>? Steps,
    IReadOnlyList<string>? Tags,
    int Priority);
