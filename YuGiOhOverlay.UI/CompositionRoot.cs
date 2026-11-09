using System.IO;
using System.Net.Http;
using System.Text.Json;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.Infrastructure;
using YuGiOhOverlay.Vision;
using YuGiOhOverlay.Windows;

namespace YuGiOhOverlay.UI;

public sealed record AssistantSettings(string SourceUrl, CaptureProfile? Capture, RecognitionOptions Recognition, CaptureProfile? FieldCapture = null)
{
    public static AssistantSettings Default => new(
        "https://www.masterduelmeta.com/combos-and-counters?combosDeckType=61ebdf8e0a39c54503c45337#combos", null, new());
}

// Manual constructor injection: the only place creating concrete application adapters.
public sealed class CompositionRoot : IDisposable
{
    private sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
    private readonly IClock _clock = new SystemClock();
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YuGiOhOverlay");
    public IComboRepository Repository { get; }
    public MasterDuelMetaClient MasterDuelMeta { get; }
    public string ImagesFolder => Path.Combine(_folder, "artwork");

    public CompositionRoot()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("YuGiOhOverlay/2.0 (+https://github.com/GetMetamorph/YuGiOhOverlay)");
        Repository = new JsonComboRepository(Path.Combine(_folder, "combos.v3.json"), Path.Combine(_folder, "combos.v2.json"));
        MasterDuelMeta = new(_http, new MasterDuelMetaParser(), _clock);
    }

    public async Task<AssistantSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var path = Path.Combine(_folder, "assistant.json");
        if (!File.Exists(path)) return AssistantSettings.Default;
        var settings = JsonSerializer.Deserialize<AssistantSettings>(await File.ReadAllTextAsync(path, ct), JsonComboRepository.SerializerOptions)
            ?? throw new InvalidDataException("Configuration vide.");
        if (settings.Recognition is null) throw new InvalidDataException("Paramètres de reconnaissance manquants.");
        settings.Recognition.Validate();
        MasterDuelMetaClient.ParseDeckId(settings.SourceUrl);
        return settings;
    }

    public async Task SaveSettingsAsync(AssistantSettings settings, CancellationToken ct)
    {
        settings.Recognition.Validate();
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "assistant.json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(settings, JsonComboRepository.SerializerOptions), ct);
        File.Move(path + ".tmp", path, true);
    }

    public Task ObserveAsync(TargetWindow target, CaptureProfile profile, RecognitionOptions options,
        ComboCatalog catalog, IOverlayPresenter presenter, GuideSession guideSession, CaptureProfile? fieldProfile, CancellationToken ct) => Task.Run(async () =>
    {
        var images = new List<(string Id, byte[] Image)>();
        foreach (var card in catalog.Cards)
        {
            ct.ThrowIfCancellationRequested();
            if (!MasterDuelMetaParser.IsObjectId(card.Id)) continue;
            var path = Path.Combine(ImagesFolder, card.Id + ".webp");
            if (!File.Exists(path)) throw new InvalidOperationException("Illustrations manquantes. Synchroniser le deck.");
            images.Add((card.Id, await File.ReadAllBytesAsync(path, ct)));
            foreach (var alternate in Directory.EnumerateFiles(ImagesFolder, card.Id + ".*.webp"))
            {
                var variantId = Path.GetFileNameWithoutExtension(alternate).Split('.').Last();
                if (MasterDuelMetaParser.IsObjectId(variantId)) images.Add((card.Id, await File.ReadAllBytesAsync(alternate, ct)));
            }
        }
        if (images.Count == 0) throw new InvalidOperationException("Aucune illustration disponible. Synchroniser depuis Master Duel Meta.");
        using var recognizer = new SiftHandRecognizer(images, options);
        var capture = new WindowCapture(target, profile, _clock, options.MaximumImagePixels);
        var engine = new RecommendationEngine(options.MinimumConfidence, TimeSpan.FromMilliseconds(options.MaximumAgeMilliseconds));
        var fieldCapture = fieldProfile is null ? null : new WindowCapture(target, fieldProfile, _clock, options.MaximumImagePixels);
        await new ObserveHand(capture, recognizer, engine, presenter, _clock, options, guideSession, fieldCapture).RunAsync(catalog, ct);
    }, ct);

    public void Dispose() => _http.Dispose();
}
