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
            return CreateEmpty();

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var data = await JsonSerializer.DeserializeAsync<AppData>(stream, _jsonOptions, ct);

            // IMPORTANT: sanitize null collections
            return Sanitize(data);
        }
        catch (JsonException)
        {
            // JSON invalide → on retombe sur une base vide plutôt que crash
            return CreateEmpty();
        }
        catch (IOException)
        {
            // fichier verrouillé etc.
            return CreateEmpty();
        }
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

    private static AppData CreateEmpty()
        => new(Version: 1, Decks: Array.Empty<DeckDefinition>());

    private static AppData Sanitize(AppData? data)
    {
        if (data is null)
            return CreateEmpty();

        var decks = data.Decks ?? Array.Empty<DeckDefinition>();

        // sanitize nested collections too
        var normalizedDecks = decks
            .Where(d => d is not null)
            .Select(d =>
            {
                var cards = d.Cards ?? Array.Empty<CardPlan>();
                var normalizedCards = cards
                    .Where(c => c is not null)
                    .Select(c =>
                    {
                        var steps = c.Steps ?? Array.Empty<string>();
                        return c with { Steps = steps };
                    })
                    .ToList();

                return d with { Cards = normalizedCards };
            })
            .ToList();

        return data with { Decks = normalizedDecks };
    }
}
