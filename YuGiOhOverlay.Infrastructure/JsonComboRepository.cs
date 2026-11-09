using System.Text.Json;
using System.Text.Json.Serialization;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public sealed class JsonComboRepository(string path) : IComboRepository
{
    public static JsonSerializerOptions SerializerOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        MaxDepth = 32
    };

    public async Task<ComboCatalog?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Catalogue trop volumineux.");
        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<ComboCatalog>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException("Catalogue vide.");
        CatalogValidator.Validate(catalog);
        return catalog;
    }

    public async Task SaveAsync(ComboCatalog catalog, CancellationToken cancellationToken)
    {
        CatalogValidator.Validate(catalog);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(catalog, SerializerOptions), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
