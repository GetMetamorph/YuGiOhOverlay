using System.Diagnostics;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Application;

public sealed class ObserveHand(
    IScreenCapture capture, IGameStateRecognizer recognizer, IRecommendationEngine engine,
    IOverlayPresenter presenter, IClock clock, RecognitionOptions options,
    GuideSession? guideSession = null, IScreenCapture? fieldCapture = null)
{
    public async Task RunAsync(ComboCatalog catalog, CancellationToken cancellationToken)
    {
        options.Validate();
        var stabilizer = new HandStabilizer(options);
        var tracker = new VisualStepTracker(options);
        long? revision = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var guide = guideSession?.Snapshot;
                if (revision != guide?.Revision) { stabilizer.Reset(); tracker.Reset(); revision = guide?.Revision; }
                try { await ObserveOnceAsync(guide); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or TimeoutException)
                {
                    stabilizer.Reset(); tracker.Reset();
                    await presenter.PresentAsync(new("Suspendu · " + ex.Message,
                        new(clock.UtcNow, [], false), [], SessionRevision: guideSession?.Snapshot.Revision,
                        Area: ObservationArea.None, GuideStatus: "Observation interrompue · étape conservée"), cancellationToken);
                }
                await Task.Delay(options.IntervalMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            await presenter.PresentAsync(new("Suspendu", new(clock.UtcNow, [], false), [],
                SessionRevision: guideSession?.Snapshot.Revision, Area: ObservationArea.None), CancellationToken.None);
        }

        async Task ObserveOnceAsync(GuideSnapshot? guide)
        {
            var following = guide?.IsFollowing == true;
            var area = following ? guide!.Cue?.Area ?? ObservationArea.None : ObservationArea.Hand;
            var selectedCapture = area == ObservationArea.AlliedField ? fieldCapture : capture;
            if (following && (!guide!.AutoAdvance || guide.Completed || area == ObservationArea.None || selectedCapture is null))
            {
                var reason = guide.Completed ? "Dernière étape observée · quitte le combo pour reprendre la recherche"
                    : !guide.AutoAdvance ? "Progression manuelle · aucune capture"
                    : area == ObservationArea.None ? "Étape à valider manuellement · résultat visuel non déterminable"
                    : "Calibre le terrain allié pour observer cette étape";
                await presenter.PresentAsync(new("Suivi du combo · recherche suspendue", new(clock.UtcNow, [], false), [],
                    SessionRevision: guide.Revision, Area: ObservationArea.None, GuideStatus: reason), cancellationToken);
                return;
            }
            var timer = Stopwatch.StartNew();
            var frame = await selectedCapture!.CaptureAsync(cancellationToken);
            var capturedMs = timer.Elapsed.TotalMilliseconds;
            var expectedIds = following ? guide!.Cue!.Cards.Select(c => c.CardId).ToHashSet(StringComparer.Ordinal) : null;
            var detections = expectedIds is not null && recognizer is ITargetedGameStateRecognizer targeted
                ? await targeted.RecognizeAsync(frame, expectedIds, cancellationToken)
                : await recognizer.RecognizeAsync(frame, cancellationToken);
            if (expectedIds is not null) detections = detections.Where(d => d.CardId is not null && expectedIds.Contains(d.CardId)).ToArray();
            var recognizedMs = timer.Elapsed.TotalMilliseconds;
            if (guideSession?.Snapshot.Revision != guide?.Revision) return;
            var state = stabilizer.Observe(frame, detections);
            var stale = frame.CapturedAt > clock.UtcNow || clock.UtcNow - frame.CapturedAt > TimeSpan.FromMilliseconds(options.MaximumAgeMilliseconds);
            IReadOnlyList<RecommendedAction> recommendations = [];
            string guideStatus = "";
            var resultRevision = guide?.Revision;
            if (following)
            {
                var arrived = tracker.Observe(guide!, frame, state.Hand, clock.UtcNow);
                guideStatus = tracker.HasBaseline ? "Prêt · réalise l’étape, puis laisse son résultat visible"
                    : "Observation initiale · attends « Prêt » avant de réaliser l’étape";
                if (arrived && guideSession!.TryAdvance(guide!.Revision))
                {
                    resultRevision = guideSession.Snapshot.Revision;
                    guideStatus = "Résultat visuel observé · étape avancée automatiquement";
                }
            }
            else if (!stale) recommendations = engine.Recommend(catalog, state, clock.UtcNow);
            var status = stale ? "Suspendu · analyse trop lente" : following ? "Suivi du combo · recherche suspendue"
                : !state.IsStable ? "Observation · stabilisation" : state.Hand.Count == 0 ? "Observation · aucune carte identifiée" : "Observation · main analysée";
            await presenter.PresentAsync(new(status, state, recommendations,
                options.Diagnostics ? frame.Png : null, timer.Elapsed.TotalMilliseconds, capturedMs,
                recognizedMs - capturedMs, timer.Elapsed.TotalMilliseconds - recognizedMs, resultRevision, area, guideStatus), cancellationToken);
        }
    }
}
