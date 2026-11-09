using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Application;

public interface IClock { DateTimeOffset UtcNow { get; } }
public interface IComboRepository
{
    Task<ComboCatalog?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(ComboCatalog catalog, CancellationToken cancellationToken);
}

public sealed record CapturedFrame(byte[] Png, DateTimeOffset CapturedAt, string GeometryKey);
public interface IScreenCapture { Task<CapturedFrame> CaptureAsync(CancellationToken cancellationToken); }
public interface IGameStateRecognizer
{
    Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken);
}
public interface ITargetedGameStateRecognizer : IGameStateRecognizer
{
    Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, IReadOnlySet<string> cardIds, CancellationToken cancellationToken);
}
public interface IOverlayPresenter { Task PresentAsync(ObservationResult result, CancellationToken cancellationToken); }

public sealed record ObservationResult(
    string Status, BoardState State, IReadOnlyList<RecommendedAction> Recommendations,
    byte[]? DiagnosticPng = null, double ElapsedMilliseconds = 0,
    double CaptureMilliseconds = 0, double RecognitionMilliseconds = 0, double DecisionMilliseconds = 0,
    long? SessionRevision = null, ObservationArea Area = ObservationArea.Hand, string GuideStatus = "");

public sealed record RecognitionOptions
{
    public int IntervalMilliseconds { get; init; } = 700;
    public int StableFrames { get; init; } = 3;
    public int MaximumAgeMilliseconds { get; init; } = 3000;
    public double MinimumConfidence { get; init; } = 0.72;
    public int Features { get; init; } = 2200;
    public double RatioTest { get; init; } = 0.72;
    public int MinimumMatches { get; init; } = 12;
    public double MinimumInlierRatio { get; init; } = 0.65;
    public double ReprojectionThreshold { get; init; } = 4;
    public double MinimumAreaRatio { get; init; } = 0.003;
    public double MaximumAreaRatio { get; init; } = 0.65;
    public double DuplicateOverlap { get; init; } = 0.55;
    public double StableZoneOverlap { get; init; } = 0.5;
    public int MaximumCopies { get; init; } = 3;
    public int MaximumImagePixels { get; init; } = 8000000;
    public NormalizedRect Artwork { get; init; } = new(0.12, 0.18, 0.76, 0.52);
    public bool Diagnostics { get; init; }

    public void Validate()
    {
        static bool Unit(double x) => double.IsFinite(x) && x > 0 && x <= 1;
        if (IntervalMilliseconds is < 250 or > 10000 || StableFrames is < 2 or > 10
            || MaximumAgeMilliseconds < IntervalMilliseconds || MaximumAgeMilliseconds > 30000
            || !Unit(MinimumConfidence) || !Unit(RatioTest) || !Unit(MinimumInlierRatio)
            || !Unit(MinimumAreaRatio) || !Unit(MaximumAreaRatio) || MinimumAreaRatio >= MaximumAreaRatio
            || !Unit(DuplicateOverlap) || !Unit(StableZoneOverlap) || Features is < 100 or > 10000
            || MinimumMatches is < 4 or > 200 || !double.IsFinite(ReprojectionThreshold)
            || ReprojectionThreshold is < 0.5 or > 10 || MaximumCopies is < 1 or > 3
            || MaximumImagePixels is < 10000 or > 16000000 || !Artwork.IsValid)
            throw new ArgumentException("Configuration de reconnaissance invalide.");
    }
}
