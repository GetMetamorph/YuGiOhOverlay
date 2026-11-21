using System.Text.Json;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Infrastructure;

namespace YuGiOhOverlay.Domain.Tests;

public sealed class CalibrationSettingsTests
{
    private static CaptureProfile Hand => new(new(.1, .8, .8, .15), 16.0 / 9, 144);
    private static CaptureProfile Field => new(new(.1, .4, .8, .3), 16.0 / 9, 144);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverlappingSelectionPreservesBothSavedProfiles(bool field)
    {
        var original = AssistantSettings.Default.WithCalibration(Hand, false).WithCalibration(Field, true);
        var overlapping = new CaptureProfile(new(.1, .6, .8, .3), 16.0 / 9, 144);
        Assert.Throws<ArgumentException>(() => original.WithCalibration(overlapping, field));
        Assert.Equal(Hand, original.Capture);
        Assert.Equal(Field, original.FieldCapture);
    }

    [Fact]
    public async Task BothZonesSurviveRestartAndRecalibratingTheHand()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "assistant.json");
            var store = new AssistantSettingsStore(path);
            var saved = AssistantSettings.Default.WithCalibration(Hand, false).WithCalibration(Field, true);
            await store.SaveAsync(saved, default);
            var restarted = await new AssistantSettingsStore(path).LoadAsync(default);
            Assert.Equal(Hand, restarted.Capture);
            Assert.Equal(Field, restarted.FieldCapture);
            var updated = restarted.WithCalibration(Hand with { Hand = new(.2, .8, .6, .18) }, false);
            await store.SaveAsync(updated, default);
            Assert.Equal(updated, await store.LoadAsync(default));
            Assert.Equal(Field, updated.FieldCapture);
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void LegacySettingsWithoutFieldRemainCompatible()
    {
        var json = JsonSerializer.SerializeToNode(AssistantSettings.Default with { Capture = Hand }, JsonComboRepository.SerializerOptions)!;
        json.AsObject().Remove("fieldCapture");
        var loaded = json.Deserialize<AssistantSettings>(JsonComboRepository.SerializerOptions)!;
        loaded.Validate();
        Assert.Equal(Hand, loaded.Capture);
        Assert.Null(loaded.FieldCapture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(double.NaN)]
    public void AnalysisScaleMustRemainBounded(double scale) =>
        Assert.Throws<ArgumentException>(() => new RecognitionOptions { MaximumAnalysisScale = scale }.Validate());

    [Fact]
    public async Task CancelledSaveDoesNotReplaceTheExistingCalibration()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AssistantSettingsStore(Path.Combine(folder, "assistant.json"));
            var original = AssistantSettings.Default.WithCalibration(Hand, false);
            await store.SaveAsync(original, default);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(original.WithCalibration(Field, true), cancellation.Token));
            Assert.Equal(original, await store.LoadAsync(default));
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
