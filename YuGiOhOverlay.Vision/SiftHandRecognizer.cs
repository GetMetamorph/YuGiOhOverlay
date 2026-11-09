using OpenCvSharp;
using OpenCvSharp.Features2D;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Vision;

// Owned by one observation session; never called concurrently. Feature descriptors
// come only from downloaded artwork, not language-dependent card names or borders.
public sealed class SiftHandRecognizer : ITargetedGameStateRecognizer, IDisposable
{
    private sealed record Template(string Id, KeyPoint[] Points, Mat Descriptors, int Width, int Height) : IDisposable
    {
        public void Dispose() => Descriptors.Dispose();
    }
    private readonly List<Template> _templates = [];
    private readonly RecognitionOptions _options;
    private readonly SIFT _features;

    public SiftHandRecognizer(IEnumerable<(string Id, byte[] Image)> images, RecognitionOptions options)
    {
        options.Validate();
        _options = options;
        _features = SIFT.Create(options.Features);
        try
        {
            foreach (var (id, bytes) in images)
            {
                using var full = Cv2.ImDecode(bytes, ImreadModes.Grayscale);
                if (full.Empty() || (long)full.Width * full.Height > options.MaximumImagePixels)
                    throw new InvalidDataException("Illustration illisible : " + id);
                var art = options.Artwork;
                var rect = new Rect((int)(full.Width * art.X), (int)(full.Height * art.Y),
                    Math.Max(1, (int)(full.Width * art.Width)), Math.Max(1, (int)(full.Height * art.Height)));
                using var crop = new Mat(full, rect);
                var descriptors = new Mat();
                _features.DetectAndCompute(crop, null, out var points, descriptors);
                _templates.Add(new(id, points, descriptors, crop.Width, crop.Height));
            }
        }
        catch { Dispose(); throw; }
    }

    public Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, CancellationToken cancellationToken)
        => RecognizeAsync(frame, null, cancellationToken);

    public Task<IReadOnlyList<CardDetection>> RecognizeAsync(CapturedFrame frame, IReadOnlySet<string>? cardIds, CancellationToken cancellationToken)
    {
        try { return Task.FromResult(Recognize(frame, cardIds, cancellationToken)); }
        catch (OpenCVException ex) { throw new InvalidOperationException("Analyse visuelle impossible.", ex); }
    }

    private IReadOnlyList<CardDetection> Recognize(CapturedFrame frame, IReadOnlySet<string>? cardIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scene = Cv2.ImDecode(frame.Png, ImreadModes.Grayscale);
        if (scene.Empty() || (long)scene.Width * scene.Height > _options.MaximumImagePixels)
            throw new InvalidDataException("Zone de capture illisible ou trop grande.");
        using var descriptors = new Mat();
        _features.DetectAndCompute(scene, null, out var points, descriptors);
        if (descriptors.Empty()) return [];
        using var matcher = new BFMatcher(NormTypes.L2);
        var detections = new List<CardDetection>();
        foreach (var template in _templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cardIds is not null && !cardIds.Contains(template.Id)) continue;
            if (template.Descriptors.Rows < _options.MinimumMatches) continue;
            // Scene -> reference permits repeated copies of the same card in different locations.
            var matches = matcher.KnnMatch(descriptors, template.Descriptors, 2)
                .Where(pair => pair.Length == 2 && pair[0].Distance < _options.RatioTest * pair[1].Distance)
                .Select(pair => pair[0]).ToList();
            for (var copy = 0; copy < _options.MaximumCopies && matches.Count >= _options.MinimumMatches; copy++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reference = matches.Select(m => template.Points[m.TrainIdx].Pt).ToArray();
                var observed = matches.Select(m => points[m.QueryIdx].Pt).ToArray();
                using var mask = new Mat();
                using var homography = Cv2.FindHomography(reference.Select(p => new Point2d(p.X, p.Y)),
                    observed.Select(p => new Point2d(p.X, p.Y)), HomographyMethods.Ransac, _options.ReprojectionThreshold, mask);
                if (homography.Empty()) break;
                var inliers = Cv2.CountNonZero(mask);
                if (inliers < _options.MinimumMatches) break;
                var corners = Cv2.PerspectiveTransform(new[] { new Point2f(0, 0), new Point2f(template.Width, 0),
                    new Point2f(template.Width, template.Height), new Point2f(0, template.Height) }, homography);
                if (!TryGetZone(corners, scene.Width, scene.Height, out var zone)) break;
                var localMatches = matches.Count(m => Inside(points[m.QueryIdx].Pt, zone, scene.Width, scene.Height));
                var ratio = Math.Min(1, inliers / (double)Math.Max(1, localMatches));
                if (ratio < _options.MinimumInlierRatio) break;
                // Heuristic geometric confidence, not a calibrated probability.
                var confidence = ratio * Math.Min(1, inliers / (double)(_options.MinimumMatches * 2));
                detections.Add(new(template.Id, confidence, zone));
                matches.RemoveAll(m => Inside(points[m.QueryIdx].Pt, zone, scene.Width, scene.Height));
            }
        }
        return detections;
    }

    private bool TryGetZone(Point2f[] corners, int width, int height, out NormalizedRect zone)
    {
        zone = default;
        if (corners.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y))) return false;
        // Reject mirrored, nonconvex or implausibly thin projective matches.
        double? sign = null;
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i]; var b = corners[(i + 1) % 4]; var c = corners[(i + 2) % 4];
            var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (cross <= 0 || sign.HasValue && Math.Sign(cross) != sign.Value) return false;
            sign = Math.Sign(cross);
        }
        var left = Math.Clamp(corners.Min(p => p.X), 0, width);
        var top = Math.Clamp(corners.Min(p => p.Y), 0, height);
        var right = Math.Clamp(corners.Max(p => p.X), 0, width);
        var bottom = Math.Clamp(corners.Max(p => p.Y), 0, height);
        var area = Math.Abs(Cv2.ContourArea(corners));
        var fraction = area / (width * (double)height);
        if (fraction < _options.MinimumAreaRatio || fraction > _options.MaximumAreaRatio
            || right <= left || bottom <= top || area < (right - left) * (bottom - top) * 0.35) return false;
        zone = new(left / width, top / height, (right - left) / width, (bottom - top) / height);
        return zone.IsValid;
    }

    private static bool Inside(Point2f point, NormalizedRect zone, int width, int height) =>
        point.X >= zone.X * width && point.X <= (zone.X + zone.Width) * width
        && point.Y >= zone.Y * height && point.Y <= (zone.Y + zone.Height) * height;

    public void Dispose()
    {
        foreach (var template in _templates) template.Dispose();
        _templates.Clear();
        _features.Dispose();
    }
}
