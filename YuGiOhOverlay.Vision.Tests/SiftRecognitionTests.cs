using OpenCvSharp;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Vision;

namespace YuGiOhOverlay.Vision.Tests;

public sealed class SiftRecognitionTests
{
    private static Mat Reference()
    {
        var image = new Mat(300, 300, MatType.CV_8UC1, Scalar.All(25));
        var random = new Random(274);
        for (int n = 0; n < 180; n++)
        {
            var point = new Point(random.Next(15, 285), random.Next(15, 285));
            Cv2.Circle(image, point, random.Next(2, 13), Scalar.All(random.Next(50, 255)), random.Next(1, 3));
        }
        Cv2.PutText(image, "SYNTHETIC A", new Point(35, 150), HersheyFonts.HersheySimplex, .7, Scalar.White, 2);
        return image;
    }
    private static byte[] Encode(Mat image) { Cv2.ImEncode(".png", image, out var bytes); return bytes; }
    private static RecognitionOptions Options => new() { Artwork = new(0, 0, 1, 1) };
    private static CapturedFrame Frame(Mat scene) => new(Encode(scene), DateTimeOffset.UtcNow, "synthetic-fixture");
    [Fact]
    public async Task ScaledRotatedArtIsIdentifiedWithCorrectLocation()
    {
        using var reference = Reference();
        using var transform = Cv2.GetRotationMatrix2D(new Point2f(150, 150), 12, .85);
        transform.Set(0, 2, transform.At<double>(0, 2) + 220);
        transform.Set(1, 2, transform.At<double>(1, 2) + 40);
        using var scene = new Mat();
        Cv2.WarpAffine(reference, scene, transform, new Size(800, 420));
        using var recognizer = new SiftHandRecognizer([("synthetic-a", Encode(reference))], Options);
        var results = await recognizer.RecognizeAsync(Frame(scene), default);
        var match = Assert.Single(results);
        Assert.Equal("synthetic-a", match.CardId);
        Assert.True(match.Confidence >= Options.MinimumConfidence);
        Assert.InRange(match.Zone.X, .22, .36);
        Assert.InRange(match.Zone.Y, .03, .2);
    }
    [Fact]
    public async Task BlankImageIsUnknown()
    {
        using var reference = Reference(); using var scene = new Mat(400, 800, MatType.CV_8UC1, Scalar.Black);
        using var recognizer = new SiftHandRecognizer([("synthetic-a", Encode(reference))], Options);
        Assert.Empty(await recognizer.RecognizeAsync(Frame(scene), default));
    }
    [Fact]
    public async Task TwoSeparateCopiesRemainTwoDetections()
    {
        using var reference = Reference(); using var scene = new Mat(400, 800, MatType.CV_8UC1, Scalar.Black);
        using (var first = new Mat(scene, new Rect(20, 50, 300, 300))) reference.CopyTo(first);
        using (var second = new Mat(scene, new Rect(450, 50, 300, 300))) reference.CopyTo(second);
        using var recognizer = new SiftHandRecognizer([("synthetic-a", Encode(reference))], Options);
        var results = await recognizer.RecognizeAsync(Frame(scene), default);
        Assert.Equal(2, results.Count);
        Assert.All(results, d => Assert.True(d.Confidence >= Options.MinimumConfidence));
    }
    [Fact]
    public async Task CancellationIsObserved()
    {
        using var reference = Reference();
        using var recognizer = new SiftHandRecognizer([("synthetic-a", Encode(reference))], Options);
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recognizer.RecognizeAsync(Frame(reference), ct.Token));
    }
}
