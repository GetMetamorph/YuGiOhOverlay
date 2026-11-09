using System.Net;
using System.Text.RegularExpressions;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.UI;

public sealed record GuideLine(string Badge, string CardName, string Detail, string Connector, bool IsBranch = false);

public static class GuideTextFormatter
{
    public static string PlainText(string text)
    {
        // Render author text, never HTML. Keep paragraph and branch boundaries.
        var lines = Regex.Replace(text, @"<\s*(br\s*/?|/p|/div)\s*>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return WebUtility.HtmlDecode(Regex.Replace(lines, "<[^>]*>", "", RegexOptions.None, TimeSpan.FromSeconds(1))).Trim();
    }
    public static IReadOnlyList<GuideLine> Format(ComboStep? step, string fallback)
    {
        if (step is null) return [new("Instruction", "", PlainText(fallback), "")];
        var lines = new List<GuideLine>();
        Add(step, lines);
        return lines;
    }
    public static string Summary(ComboStep? step, string fallback)
    {
        var first = step?.Instructions.FirstOrDefault();
        return first is null ? PlainText(fallback) : first.Action + " · " + first.CardName;
    }
    private static void Add(ComboStep step, List<GuideLine> lines)
    {
        foreach (var instruction in step.Instructions)
            lines.Add(new(instruction.Action, PlainText(instruction.CardName), PlainText(instruction.Detail), instruction.Connector switch
            {
                "arrow" => "Puis ↓",
                "plus" => "+",
                "or" => "OU",
                "equals" => "Résultat ↓",
                "" => "",
                _ => instruction.Connector
            }));
        foreach (var branch in step.Branches)
        {
            lines.Add(new("CHOIX MANUEL", PlainText(branch.Label), PlainText(step.ConditionLabel), "", true));
            foreach (var child in branch.Steps) Add(child, lines);
        }
    }
}
