using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.UI;

public sealed record CardListItem(
    string CardId,
    string Name,
    int Priority,
    IReadOnlyList<string> Tags,
    CardPlan Source);
