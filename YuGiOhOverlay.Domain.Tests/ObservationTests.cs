using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Domain.Tests;

public sealed class ObservationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DiagnosticCandidatesNeverBypassConfidenceThreshold(bool diagnostics)
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new TestClock();
        var presenter = new TestPresenter(cancellation);
        var options = new RecognitionOptions { IntervalMilliseconds = 250, Diagnostics = diagnostics };
        var useCase = new ObserveHand(new TestCapture(clock), new LowConfidenceRecognizer(),
            new RecommendationEngine(.72, TimeSpan.FromSeconds(3)), presenter, clock, options);
        await useCase.RunAsync(RecommendationTests.Catalog(RecommendationTests.Rule()), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(presenter.Results, r => Assert.Empty(r.Recommendations));
        var first = presenter.Results.First();
        Assert.Empty(first.State.Hand);
        if (diagnostics) Assert.Equal(.5, Assert.Single(first.DiagnosticDetections!).Confidence);
        else { Assert.Null(first.DiagnosticDetections); Assert.Null(first.DiagnosticPng); }
    }

    private sealed class LowConfidenceRecognizer : IGameStateRecognizer
    {
        public Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CardDetection>>([RecommendationTests.Detection() with { Confidence = .5 }]);
    }

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
