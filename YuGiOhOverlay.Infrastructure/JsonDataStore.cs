using System.Text.Json;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public interface IDataStore
{
    Task<AppData> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppData data, CancellationToken ct);
}

public sealed class JsonDataStore : IDataStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonDataStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        _filePath = filePath;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task<AppData> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
            return new AppData(Version: 1, Decks: Array.Empty<DeckDefinition>());

        await using var stream = File.OpenRead(_filePath);
        var data = await JsonSerializer.DeserializeAsync<AppData>(stream, _jsonOptions, ct);

        return data ?? new AppData(Version: 1, Decks: Array.Empty<DeckDefinition>());
    }

    public async Task SaveAsync(AppData data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, data, _jsonOptions, ct);
    }
}
