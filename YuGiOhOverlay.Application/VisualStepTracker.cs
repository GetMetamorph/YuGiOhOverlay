using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Application;

// Tracks changes relative to a stable baseline, not the mere presence of a card.
// A new step always needs its own baseline; one frame cannot complete two steps.
public sealed class VisualStepTracker(RecognitionOptions options)
{
    private long _revision = -1;
    private string? _geometry;
    private DateTimeOffset? _lastTime;
    private int[]? _candidate;
    private int[]? _baseline;
    private int _streak;
    private int _confirmed;
    public bool HasBaseline => _baseline is not null;
    public void Reset() { _revision = -1; _geometry = null; _lastTime = null; _candidate = null; _baseline = null; _streak = 0; _confirmed = 0; }

    public bool Observe(GuideSnapshot guide, CapturedFrame frame, IReadOnlyList<CardDetection> detections, DateTimeOffset now)
    {
        if (!guide.AutoAdvance || guide.Cue is not { } cue || guide.Completed) { Reset(); return false; }
        if (frame.CapturedAt > now || now - frame.CapturedAt > TimeSpan.FromMilliseconds(options.MaximumAgeMilliseconds)) { Reset(); return false; }
        if (_revision != guide.Revision || _geometry != frame.GeometryKey || _lastTime is { } last
            && (frame.CapturedAt <= last || frame.CapturedAt - last > TimeSpan.FromMilliseconds(options.MaximumAgeMilliseconds))) Reset();
        _revision = guide.Revision; _geometry = frame.GeometryKey; _lastTime = frame.CapturedAt;
        // The observation pipeline has already rejected overlaps and ambiguous identities.
        var counts = cue.Cards.Select(r => detections.Count(d => d.CardId == r.CardId && d.Zone.IsValid
            && double.IsFinite(d.Confidence) && d.Confidence >= options.MinimumConfidence && d.Confidence <= 1)).ToArray();
        if (_baseline is null)
        {
            _streak = _candidate is not null && counts.SequenceEqual(_candidate) ? _streak + 1 : 1;
            _candidate = counts;
            if (_streak >= options.StableFrames) _baseline = counts;
            return false;
        }
        var arrived = cue.Cards.Select((r, i) => counts[i] >= _baseline[i] + r.Count).All(value => value);
        _confirmed = arrived ? _confirmed + 1 : 0;
        if (_confirmed < options.StableFrames) return false;
        Reset();
        return true;
    }
}
