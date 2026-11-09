using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public static class CatalogMigration
{
    public static ComboCatalog Upgrade(ComboCatalog catalog)
    {
        CatalogValidator.Validate(catalog);
        if (catalog.Version == 3) return catalog;
        // Text-only v2 guides remain usable, but no visual outcome is invented.
        return catalog with
        {
            Version = 3,
            Rules = catalog.Rules.Select(rule => rule with
            {
                GuideSteps = rule.Actions.Select(text => new ComboStep(
                    [new(null, "", "Instruction", text, "")], "", [])).ToArray()
            }).ToArray()
        };
    }
}
