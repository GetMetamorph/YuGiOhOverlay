using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Domain.Tests;

public sealed class GuideObservationTests
{
    [Fact]
    public async Task FollowingObservesOnlyExpectedFieldCardsThenExitRestabilizesHand()
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new Clock(); var guide = new GuideSession(); guide.Follow(GuideProgressTests.GuideRule());
        var hand = new Capture(clock, "hand"); var field = new Capture(clock, "field");
        var vision = new Recognizer(); var engine = new Engine();
        var results = new List<ObservationResult>();
        var presenter = new Presenter(result =>
        {
            results.Add(result);
            if (guide.Snapshot.Completed) guide.Exit();
            if (result.Recommendations.Count > 0) cancellation.Cancel();
        });
        await new ObserveHand(hand, vision, engine, presenter, clock,
            new RecognitionOptions { IntervalMilliseconds = 250 }, guide, field)
            .RunAsync(RecommendationTests.Catalog(RecommendationTests.Rule()), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(6, field.Calls); Assert.Equal(3, hand.Calls); Assert.Equal(3, engine.Calls);
        Assert.Equal(6, vision.TargetedCalls);
        Assert.All(results.Where(r => r.Area == ObservationArea.AlliedField), r => Assert.Empty(r.Recommendations));
    }
    [Fact]
    public async Task ManualFollowDoesNotCaptureOrEvaluateStarters()
    {
        using var cancellation = new CancellationTokenSource(); var clock = new Clock();
        var guide = new GuideSession(); guide.Follow(RecommendationTests.Rule());
        var capture = new Capture(clock, "hand"); var engine = new Engine();
        await new ObserveHand(capture, new Recognizer(), engine, new Presenter(_ => cancellation.Cancel()), clock,
            new RecognitionOptions { IntervalMilliseconds = 250 }, guide)
            .RunAsync(RecommendationTests.Catalog(), cancellation.Token);
        Assert.Equal(0, capture.Calls); Assert.Equal(0, engine.Calls);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = RecommendationTests.Now; }
    private sealed class Capture(Clock clock, string geometry) : IScreenCapture
    {
        public int Calls { get; private set; }
        public Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken)
        {
            Calls++; clock.UtcNow = clock.UtcNow.AddMilliseconds(250);
            return Task.FromResult(new CapturedFrame([(byte)Calls], clock.UtcNow, geometry));
        }
    }
    private sealed class Recognizer : ITargetedGameStateRecognizer
    {
        public int TargetedCalls { get; private set; }
        public Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CardDetection>>([RecommendationTests.Detection()]);
        public Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, IReadOnlySet<string> ids, CancellationToken cancellationToken)
        {
            TargetedCalls++; Assert.Equal("field", frame.GeometryKey); Assert.Equal("a", Assert.Single(ids));
            return Task.FromResult<IReadOnlyList<CardDetection>>(frame.Png[0] <= 3 ? [] : [RecommendationTests.Detection()]);
        }
    }
    private sealed class Engine : IRecommendationEngine
    {
        public int Calls { get; private set; }
        public IReadOnlyList<RecommendedAction> Recommend(ComboCatalog catalog, BoardState state, DateTimeOffset now)
        { Calls++; return new RecommendationEngine(.72, TimeSpan.FromSeconds(3)).Recommend(catalog, state, now); }
    }
    private sealed class Presenter(Action<ObservationResult> display) : IOverlayPresenter
    {
        public Task PresentAsync(ObservationResult result, CancellationToken cancellationToken) { display(result); return Task.CompletedTask; }
    }
}
