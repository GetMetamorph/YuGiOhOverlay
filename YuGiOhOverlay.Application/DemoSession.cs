using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Application;

// Exercises the same observation orchestration with explicit synthetic adapters.
// It has no dependency on Windows, a filesystem, networking, or input synthesis.
public sealed class DemoSession
{
    public ComboCatalog Catalog { get; } = new(2, "demo", "DÉMO · main simulée", DateTimeOffset.UnixEpoch,
        [new("demo-a", "Carte Démo A"), new("demo-b", "Carte Démo B")],
        [new("demo-combo", "Découvrir le guide de combo", 1, new([[new("demo-a", 1), new("demo-b", 1)]]),
            ["Les deux cartes fictives ont été identifiées sur plusieurs observations.",
             "Clique sur Suivre ce combo : le guide reste affiché même lorsque les suggestions changent.",
             "Utilise les flèches ou Ctrl+→ / Ctrl+← pour avancer. Ces raccourcis ne cliquent jamais dans le jeu."],
            "", "Démo locale", "Ces données sont fictives et ne décrivent aucun combo Yu-Gi-Oh!.", [], [])], []);
    public Task RunAsync(IOverlayPresenter presenter, CancellationToken cancellationToken)
    {
        var clock = new DemoClock();
        var options = new RecognitionOptions();
        return new ObserveHand(new DemoCapture(clock), new DemoRecognizer(),
            new RecommendationEngine(options.MinimumConfidence, TimeSpan.FromMilliseconds(options.MaximumAgeMilliseconds)),
            new DemoPresenter(presenter), clock, options).RunAsync(Catalog, cancellationToken);
    }
    private sealed class DemoClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class DemoCapture(IClock clock) : IScreenCapture
    {
        public Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(new CapturedFrame([], clock.UtcNow, "demo"));
    }
    private sealed class DemoRecognizer : IGameStateRecognizer
    {
        public Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CardDetection>>([new("demo-a", .96, new(.1, .1, .25, .8)), new("demo-b", .94, new(.5, .1, .25, .8))]);
    }
    private sealed class DemoPresenter(IOverlayPresenter presenter) : IOverlayPresenter
    {
        public Task PresentAsync(ObservationResult result, CancellationToken cancellationToken) =>
            presenter.PresentAsync(result with { Status = "DÉMO · " + result.Status }, cancellationToken);
    }
}
