using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Domain.Tests;

public sealed class RecommendationTests
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
    internal static ComboRule Rule(string id = "rule", int priority = 0, params CardRequirement[] requirements) =>
        new(id, id, priority, new([requirements.Length == 0 ? [new("a", 1)] : requirements]),
            ["Demonstration instruction"], "https://www.masterduelmeta.com/combo/example", "Test", "", [], []);
    internal static ComboCatalog Catalog(params ComboRule[] rules) => new(2, "deck", "Demo", Now, [new("a", "A"), new("b", "B")], rules, []);
    internal static CardDetection Detection(string? id = "a", double score = .95, double x = 0) => new(id, score, new(x, 0, .2, .4));
    private readonly RecommendationEngine _engine = new(.72, TimeSpan.FromSeconds(3));

    [Fact]
    public void MatchingStarterProducesExplainedGuide()
    {
        var result = Assert.Single(_engine.Recommend(Catalog(Rule()), new(Now, [Detection()], true), Now));
        Assert.Equal(.95, result.Confidence);
        Assert.Contains("terrain", result.Explanation);
    }
    [Fact]
    public void EveryRequiredCardAndCopyMustBePresent()
    {
        var catalog = Catalog(Rule("two", 0, new("a", 2), new("b", 1)));
        Assert.Empty(_engine.Recommend(catalog, new(Now, [Detection(), Detection("b", x: .5)], true), Now));
        Assert.Single(_engine.Recommend(catalog, new(Now, [Detection(), Detection(x: .25), Detection("b", x: .5)], true), Now));
    }
    [Fact]
    public void OrAlternativesAreNotTreatedAsAnd()
    {
        var rule = Rule() with { Conditions = new([[new("a", 1)], [new("b", 1)]]) };
        Assert.Single(_engine.Recommend(Catalog(rule), new(Now, [Detection("b")], true), Now));
    }
    [Fact]
    public void PriorityThenOrdinalIdIsDeterministic()
    {
        var result = _engine.Recommend(Catalog(Rule("z", 5), Rule("b", 8), Rule("a", 8)), new(Now, [Detection()], true), Now);
        Assert.Equal(new[] { "a", "b", "z" }, result.Select(r => r.Combo.Id));
    }
    [Theory]
    [InlineData(null, .99)]
    [InlineData("a", .71)]
    [InlineData("a", double.NaN)]
    [InlineData("a", 1.1)]
    public void UnknownOrUntrustedCardCannotRecommend(string? id, double score) =>
        Assert.Empty(_engine.Recommend(Catalog(Rule()), new(Now, [Detection(id, score)], true), Now));
    [Theory]
    [InlineData(-4)]
    [InlineData(1)]
    public void StaleOrFutureStateCannotRecommend(int seconds) =>
        Assert.Empty(_engine.Recommend(Catalog(Rule()), new(Now.AddSeconds(seconds), [Detection()], true), Now));
    [Fact]
    public void UnstableStateCannotRecommend() =>
        Assert.Empty(_engine.Recommend(Catalog(Rule()), new(Now, [Detection()], false), Now));
    [Fact]
    public void KnownGarnetBlocksCombo() =>
        Assert.Empty(_engine.Recommend(Catalog(Rule() with { ExcludedCardIds = ["b"] }), new(Now, [Detection(), Detection("b", x: .5)], true), Now));
}

public sealed class StabilizationTests
{
    private readonly RecognitionOptions _options = new();
    private static CapturedFrame Frame(int number, string geometry = "target:1920:1080") => new([], RecommendationTests.Now.AddMilliseconds(number * 700), geometry);
    [Fact]
    public void NeedsThreeConsecutiveFramesAndDropsMissingCardsImmediately()
    {
        var stabilizer = new HandStabilizer(_options);
        Assert.False(stabilizer.Observe(Frame(0), [RecommendationTests.Detection()]).IsStable);
        Assert.False(stabilizer.Observe(Frame(1), [RecommendationTests.Detection()]).IsStable);
        Assert.True(stabilizer.Observe(Frame(2), [RecommendationTests.Detection()]).IsStable);
        var missing = stabilizer.Observe(Frame(3), []);
        Assert.Empty(missing.Hand); Assert.False(missing.IsStable);
    }
    [Fact]
    public void OverlappingDifferentIdentitiesAreUnknown()
    {
        var state = new HandStabilizer(_options).Observe(Frame(0), [RecommendationTests.Detection(), RecommendationTests.Detection("b")]);
        Assert.Empty(state.Hand);
    }
    [Fact]
    public void DuplicateBoxesDoNotCountAsTwoCopies()
    {
        var state = new HandStabilizer(_options).Observe(Frame(0), [RecommendationTests.Detection(), RecommendationTests.Detection()]);
        Assert.Single(state.Hand);
    }
    [Fact]
    public void GeometryChangeGapAndResetRestartStabilization()
    {
        var stabilizer = new HandStabilizer(_options);
        for (var n = 0; n < 3; n++) stabilizer.Observe(Frame(n), [RecommendationTests.Detection()]);
        Assert.False(stabilizer.Observe(Frame(3, "different"), [RecommendationTests.Detection()]).IsStable);
        Assert.False(stabilizer.Observe(Frame(10, "different"), [RecommendationTests.Detection()]).IsStable);
        stabilizer.Reset();
        Assert.False(stabilizer.Observe(Frame(11, "different"), [RecommendationTests.Detection()]).IsStable);
    }
    [Fact]
    public void CopyCountChangeIsNotImmediatelyStable()
    {
        var stabilizer = new HandStabilizer(_options);
        for (var n = 0; n < 3; n++) stabilizer.Observe(Frame(n), [RecommendationTests.Detection()]);
        Assert.False(stabilizer.Observe(Frame(3), [RecommendationTests.Detection(), RecommendationTests.Detection(x: .4)]).IsStable);
    }
    [Fact]
    public void InvalidOptionsFailBeforeObserving() =>
        Assert.Throws<ArgumentException>(() => (new RecognitionOptions { MinimumConfidence = double.NaN }).Validate());
}
