using System.Text.Json;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public sealed class MasterDuelMetaClient(HttpClient http, MasterDuelMetaParser parser, IClock clock)
{
    public static string ParseDeckId(string url)
    {
        if (!CatalogValidator.IsSourceUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.AbsolutePath.TrimEnd('/') != "/combos-and-counters")
            throw new ArgumentException("Colle le lien Combos & Counters de Master Duel Meta avec un deck sélectionné.");
        var ids = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == "combosDeckType").Select(p => Uri.UnescapeDataString(p[1])).ToArray();
        if (ids.Length != 1 || !MasterDuelMetaParser.IsObjectId(ids[0]))
            throw new ArgumentException("Le lien doit contenir un unique combosDeckType valide.");
        return ids[0];
    }

    public async Task<ComboCatalog> ImportAsync(string url, CancellationToken cancellationToken)
    {
        var deckId = ParseDeckId(url);
        var rows = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        const int pageSize = 20;
        for (var page = 1; page <= 25; page++)
        {
            var bytes = await DownloadBoundedAsync(
                $"https://www.masterduelmeta.com/api/v1/combos/paginated?deckType={deckId}&sort=netVotes&limit={pageSize}&page={page}",
                8 * 1024 * 1024, cancellationToken);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 });
            var root = document.RootElement;
            var data = root.GetProperty("data");
            var total = root.GetProperty("total").GetInt32();
            if (total is < 0 or > 500 || data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Réponse Master Duel Meta trop volumineuse ou inattendue.");
            foreach (var row in data.EnumerateArray())
            {
                var id = row.GetProperty("_id").GetString()!;
                if (!MasterDuelMetaParser.IsObjectId(id) || !seen.Add(id))
                    throw new InvalidDataException("Pagination Master Duel Meta incohérente. Réessayer la synchronisation.");
                rows.Add(row.Clone());
            }
            if (rows.Count >= total) return parser.Parse(rows, deckId, clock.UtcNow);
            if (data.GetArrayLength() == 0) throw new InvalidDataException("Page de combos incomplète.");
        }
        throw new InvalidDataException("Limite de pagination atteinte.");
    }

    public async Task CacheImagesAsync(ComboCatalog catalog, string folder, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        foreach (var card in catalog.Cards)
            await CacheImageAsync(card.Id, card.Id, card.Name, folder, progress, cancellationToken);

        // Same public card filter used by the website. Canonical combo IDs stay stable
        // even when a cosmetic alternate artwork has a different database ID.
        var alternateBytes = await DownloadBoundedAsync(
            "https://www.masterduelmeta.com/api/v1/cards?alternateArt=true&fields=_id,name&limit=0",
            2 * 1024 * 1024, cancellationToken);
        using var alternates = JsonDocument.Parse(alternateBytes);
        if (alternates.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Liste des illustrations alternatives invalide.");
        foreach (var art in alternates.RootElement.EnumerateArray())
        {
            var id = art.GetProperty("_id").GetString()!;
            var name = art.GetProperty("name").GetString();
            foreach (var card in catalog.Cards.Where(c => c.Name == name && c.Id != id))
                await CacheImageAsync(id, card.Id + "." + id, card.Name, folder, progress, cancellationToken);
        }
    }

    private async Task CacheImageAsync(string imageId, string fileId, string name, string folder,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!MasterDuelMetaParser.IsObjectId(imageId) || fileId.Split('.').Any(id => !MasterDuelMetaParser.IsObjectId(id)))
            throw new InvalidDataException("Identifiant d'image invalide.");
        var path = Path.Combine(folder, fileId + ".webp");
        if (File.Exists(path) && new FileInfo(path).Length > 0) return;
        progress?.Report("Illustration : " + name);
        var bytes = await DownloadBoundedAsync($"https://s3.duellinksmeta.com/cards/{imageId}_w420.webp", 2 * 1024 * 1024, cancellationToken);
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Illustration Master Duel Meta invalide.");
        await File.WriteAllBytesAsync(path + ".tmp", bytes, cancellationToken);
        File.Move(path + ".tmp", path, true);
    }

    private async Task<byte[]> DownloadBoundedAsync(string url, int limit, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Réponse distante trop volumineuse.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Réponse distante trop volumineuse.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
