using System.Text.Json;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public interface ISettingsStore
{
    Task<AppSettings?> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public JsonSettingsStore()
    {
        _filePath = Path.Combine(
            AppContext.BaseDirectory,
            "settings.json");
    }

    public async Task<AppSettings?> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return null;

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, _options, ct);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, _options, ct);
    }
}