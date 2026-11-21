using System.Text.Json;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public sealed record AssistantSettings(string SourceUrl, CaptureProfile? Capture, RecognitionOptions Recognition, CaptureProfile? FieldCapture = null)
{
    public static AssistantSettings Default => new(
        "https://www.masterduelmeta.com/combos-and-counters?combosDeckType=61ebdf8e0a39c54503c45337#combos", null, new());

    public AssistantSettings WithCalibration(CaptureProfile profile, bool field)
    {
        profile.Validate();
        var other = field ? Capture : FieldCapture;
        if (other is not null && other.Hand.Overlap(profile.Hand) > 0)
            throw new ArgumentException("La main et le terrain se chevauchent. Retrace la zone sans inclure la zone orange ; les zones enregistrées sont conservées.");
        return field ? this with { FieldCapture = profile } : this with { Capture = profile };
    }

    public void Validate()
    {
        if (Recognition is null) throw new InvalidDataException("Paramètres de reconnaissance manquants.");
        Recognition.Validate();
        Capture?.Validate();
        FieldCapture?.Validate();
        MasterDuelMetaClient.ValidateImportUrl(SourceUrl);
    }
}

public sealed class AssistantSettingsStore(string path)
{
    public async Task<AssistantSettings> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(path)) return AssistantSettings.Default;
        var settings = JsonSerializer.Deserialize<AssistantSettings>(await File.ReadAllTextAsync(path, ct), JsonComboRepository.SerializerOptions)
            ?? throw new InvalidDataException("Configuration vide.");
        settings.Validate();
        return settings;
    }

    public async Task SaveAsync(AssistantSettings settings, CancellationToken ct)
    {
        settings.Validate();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, JsonComboRepository.SerializerOptions), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
