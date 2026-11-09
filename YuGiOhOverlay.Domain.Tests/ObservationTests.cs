using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Domain.Tests;

public sealed class ObservationTests
{
    [Fact]
    public async Task SimulatedPipelineStabilizesThenClearsOnCaptureFailureAndStops()
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new TestClock();
        var presenter = new TestPresenter(cancellation);
        var options = new RecognitionOptions { IntervalMilliseconds = 250 };
        var useCase = new ObserveHand(new TestCapture(clock), new TestRecognizer(),
            new RecommendationEngine(.72, TimeSpan.FromSeconds(3)), presenter, clock, options);
        await useCase.RunAsync(RecommendationTests.Catalog(RecommendationTests.Rule()), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(presenter.Results, r => r.Recommendations.Count == 1);
        var suspended = presenter.Results.First(r => r.Status.Contains("fenêtre"));
        Assert.Empty(suspended.State.Hand); Assert.Empty(suspended.Recommendations);
        Assert.Equal("Suspendu", presenter.Results.Last().Status);
        Assert.All(presenter.Results, result => Assert.Null(result.DiagnosticPng));
    }
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = RecommendationTests.Now;
    }
    private sealed class TestCapture(TestClock clock) : IScreenCapture
    {
        private int _count;
        public Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken)
        {
            if (++_count == 4) throw new InvalidOperationException("fenêtre indisponible");
            clock.UtcNow = clock.UtcNow.AddMilliseconds(250);
            return Task.FromResult(new CapturedFrame([], clock.UtcNow, "simulation"));
        }
    }
    private sealed class TestRecognizer : IGameStateRecognizer
    {
        public Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CardDetection>>([RecommendationTests.Detection()]);
    }
    private sealed class TestPresenter(CancellationTokenSource cancellation) : IOverlayPresenter
    {
        public List<ObservationResult> Results { get; } = [];
        public Task PresentAsync(ObservationResult result, CancellationToken cancellationToken)
        {
            Results.Add(result);
            if (result.Status.Contains("fenêtre")) cancellation.Cancel();
            return Task.CompletedTask;
        }
    }
}
