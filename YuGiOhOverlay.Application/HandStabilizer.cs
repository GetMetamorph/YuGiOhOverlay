using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Application;

public sealed class HandStabilizer(RecognitionOptions options)
{
    private IReadOnlyList<CardDetection> _previous = [];
    private string? _geometry;
    private DateTimeOffset? _lastFrame;
    private int _streak;

    public void Reset() { _previous = []; _geometry = null; _streak = 0; _lastFrame = null; }

    public BoardState Observe(CapturedFrame frame, IReadOnlyList<CardDetection> detections)
    {
        // Ambiguous overlapping identities are discarded, never resolved by a forced match.
        var eligible = detections.Where(d => d.CardId is not null && d.Zone.IsValid
            && double.IsFinite(d.Confidence) && d.Confidence >= options.MinimumConfidence && d.Confidence <= 1).ToArray();
        var current = eligible.Where(d => !eligible.Any(other => other.CardId != d.CardId
            && other.Zone.Overlap(d.Zone) >= options.DuplicateOverlap))
            .OrderByDescending(d => d.Confidence).Aggregate(new List<CardDetection>(), (list, detection) =>
            {
                if (!list.Any(d => d.Zone.Overlap(detection.Zone) >= options.DuplicateOverlap)) list.Add(detection);
                return list;
            });
        var remaining = _previous.ToList();
        bool same = _geometry == frame.GeometryKey && _lastFrame is { } last
            && frame.CapturedAt > last && frame.CapturedAt - last <= TimeSpan.FromMilliseconds(options.MaximumAgeMilliseconds)
            && current.Count == remaining.Count;
        if (same)
        {
            foreach (var detection in current)
            {
                var index = remaining.FindIndex(d => d.CardId == detection.CardId
                    && d.Zone.Overlap(detection.Zone) >= options.StableZoneOverlap);
                if (index < 0) { same = false; break; }
                remaining.RemoveAt(index);
            }
        }
        _streak = same ? _streak + 1 : 1;
        _previous = current;
        _geometry = frame.GeometryKey;
        _lastFrame = frame.CapturedAt;
        return new(frame.CapturedAt, current, _streak >= options.StableFrames);
    }
}
