using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Domain.Tests;

public sealed class GuideProgressTests
{
    internal static ComboRule GuideRule(ObservationArea area = ObservationArea.AlliedField) => RecommendationTests.Rule() with
    {
        GuideSteps = [new([new("a", "Card A", "Invoquer", "", "")], "", [], new(area, [new("a", 1)]))]
    };
    private readonly RecognitionOptions _options = new();
    private DateTimeOffset _time = RecommendationTests.Now;
    private bool Observe(VisualStepTracker tracker, GuideSnapshot guide, int count, string geometry = "field", double confidence = .96)
    {
        _time = _time.AddMilliseconds(700);
        var detections = Enumerable.Range(0, count).Select(i => new CardDetection("a", confidence, new(.1 + i * .3, .1, .2, .3))).ToArray();
        return tracker.Observe(guide, new([], _time, geometry), detections, _time);
    }
    [Fact]
    public void StableArrivalAdvancesOnlyOnceAndKeepsGuideUntilExit()
    {
        var session = new GuideSession(); session.Follow(GuideRule());
        var guide = session.Snapshot; var tracker = new VisualStepTracker(_options);
        for (int i = 0; i < 3; i++) Assert.False(Observe(tracker, guide, 0));
        Assert.False(Observe(tracker, guide, 1)); Assert.False(Observe(tracker, guide, 1));
        Assert.True(Observe(tracker, guide, 1));
        Assert.True(session.TryAdvance(guide.Revision));
        Assert.True(session.Snapshot.Completed); Assert.True(session.Snapshot.IsFollowing);
        Assert.False(session.TryAdvance(guide.Revision));
        session.Exit(); Assert.False(session.Snapshot.IsFollowing);
    }
    [Fact]
    public void CardAlreadyPresentIsNotCompletion()
    {
        var session = new GuideSession(); session.Follow(GuideRule());
        var tracker = new VisualStepTracker(_options);
        for (int i = 0; i < 10; i++) Assert.False(Observe(tracker, session.Snapshot, 1));
    }
    [Fact]
    public void LowConfidenceFlickerAndGeometryChangeDoNotAdvance()
    {
        var session = new GuideSession(); session.Follow(GuideRule()); var tracker = new VisualStepTracker(_options);
        for (int i = 0; i < 3; i++) Assert.False(Observe(tracker, session.Snapshot, 0));
        Assert.False(Observe(tracker, session.Snapshot, 1));
        Assert.False(Observe(tracker, session.Snapshot, 1, confidence: .4));
        Assert.False(Observe(tracker, session.Snapshot, 1));
        for (int i = 0; i < 6; i++) Assert.False(Observe(tracker, session.Snapshot, 1, "resized"));
    }
    [Fact]
    public void ManualMovementExitAndDisablingRejectPendingAdvance()
    {
        var session = new GuideSession(); session.Follow(GuideRule());
        var old = session.Snapshot.Revision; session.Move(-1); Assert.False(session.TryAdvance(old));
        old = session.Snapshot.Revision; session.SetAutoAdvance(false); Assert.False(session.TryAdvance(old));
        Assert.False(session.TryAdvance(session.Snapshot.Revision));
        session.Exit(); Assert.False(session.TryAdvance(session.Snapshot.Revision));
    }
    [Theory]
    [InlineData(-10000)]
    [InlineData(10000)]
    public void StaleOrFutureFrameResetsBaseline(int offset)
    {
        var session = new GuideSession(); session.Follow(GuideRule()); var tracker = new VisualStepTracker(_options);
        for (int i = 0; i < 3; i++) Observe(tracker, session.Snapshot, 0);
        Assert.False(tracker.Observe(session.Snapshot, new([], _time.AddMilliseconds(offset), "field"), [], _time));
        for (int i = 0; i < 6; i++) Assert.False(Observe(tracker, session.Snapshot, 1));
    }
    [Fact]
    public void NewStepCannotReuseThePreviousBaseline()
    {
        var session = new GuideSession(); var rule = GuideRule();
        session.Follow(rule with { Actions = ["First", "Second"], GuideSteps = [rule.GuideSteps![0], rule.GuideSteps[0]] });
        var tracker = new VisualStepTracker(_options);
        for (int i = 0; i < 3; i++) Observe(tracker, session.Snapshot, 0);
        session.Move(1);
        for (int i = 0; i < 6; i++) Assert.False(Observe(tracker, session.Snapshot, 1));
    }
}
