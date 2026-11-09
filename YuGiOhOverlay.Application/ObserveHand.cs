using System.Diagnostics;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Application;

public sealed class ObserveHand(
    IScreenCapture capture, IGameStateRecognizer recognizer, IRecommendationEngine engine,
    IOverlayPresenter presenter, IClock clock, RecognitionOptions options)
{
    public async Task RunAsync(ComboCatalog catalog, CancellationToken cancellationToken)
    {
        options.Validate();
        var stabilizer = new HandStabilizer(options);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    var frame = await capture.CaptureAsync(cancellationToken);
                    var capturedMs = timer.Elapsed.TotalMilliseconds;
                    var detections = await recognizer.RecognizeAsync(frame, cancellationToken);
                    var recognizedMs = timer.Elapsed.TotalMilliseconds;
                    var state = stabilizer.Observe(frame, detections);
                    var recommendations = engine.Recommend(catalog, state, clock.UtcNow);
                    var stale = clock.UtcNow - frame.CapturedAt > TimeSpan.FromMilliseconds(options.MaximumAgeMilliseconds);
                    var status = stale ? "Suspendu · analyse trop lente" : !state.IsStable ? "Observation · stabilisation"
                        : state.Hand.Count == 0 ? "Observation · aucune carte identifiée" : "Observation · main analysée";
                    await presenter.PresentAsync(new(status, state, recommendations,
                        options.Diagnostics ? frame.Png : null, timer.Elapsed.TotalMilliseconds, capturedMs,
                        recognizedMs - capturedMs, timer.Elapsed.TotalMilliseconds - recognizedMs), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
                {
                    stabilizer.Reset();
                    await presenter.PresentAsync(new("Suspendu · " + ex.Message,
                        new(clock.UtcNow, [], false), []), cancellationToken);
                }
                await Task.Delay(options.IntervalMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            await presenter.PresentAsync(new("Suspendu", new(clock.UtcNow, [], false), []), CancellationToken.None);
        }
    }
}
