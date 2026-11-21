using System.Text.Json;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.Infrastructure;

public sealed class MasterDuelMetaClient(HttpClient http, MasterDuelMetaParser parser, IClock clock)
{
    internal static void ValidateImportUrl(string url) => ParseImportUrl(url);

    private static (string Id, bool IsSingleCombo) ParseImportUrl(string url)
    {
        if (!CatalogValidator.IsSourceUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("Colle un lien Master Duel Meta vers un combo ou vers Combos & Counters avec un deck sélectionné.");

        const string comboPrefix = "/combo/";
        if (uri.AbsolutePath.StartsWith(comboPrefix, StringComparison.Ordinal))
        {
            var comboId = uri.AbsolutePath[comboPrefix.Length..].TrimEnd('/');
            if (!MasterDuelMetaParser.IsObjectId(comboId))
                throw new ArgumentException("Lien du combo invalide. Copie l’adresse complète depuis « View Full Combo ».");
            return (comboId, true);
        }

        var ids = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == "combosDeckType").Select(p => Uri.UnescapeDataString(p[1])).ToArray();
        if (ids.Length == 0)
            throw new ArgumentException("Dans Combos & Counters, ouvre l’onglet Combos, sélectionne ton deck, puis recopie l’adresse.");
        if (ids.Length != 1 || !MasterDuelMetaParser.IsObjectId(ids[0]))
            throw new ArgumentException("Le deck sélectionné dans ce lien est invalide. Sélectionne-le de nouveau sur Master Duel Meta.");
        return (ids[0], false);
    }

    public async Task<ComboCatalog> ImportAsync(string url, CancellationToken cancellationToken)
    {
        var (id, isSingleCombo) = ParseImportUrl(url);
        var catalog = isSingleCombo ? await ImportComboAsync(id, cancellationToken) : await ImportDeckAsync(id, cancellationToken);
        if (catalog.Rules.Count > 0) return catalog;
        if (catalog.ImportWarnings.Count == 0)
            throw new InvalidDataException("Master Duel Meta ne contient aucun combo pour ce deck.");
        throw new InvalidDataException($"Aucun des {catalog.ImportWarnings.Count} combos ne peut être utilisé pour la détection automatique. Exemple : {catalog.ImportWarnings[0]}.");
    }

    private async Task<ComboCatalog> ImportComboAsync(string comboId, CancellationToken cancellationToken)
    {
        // Endpoint used by Master Duel Meta's /combo/{id} page.
        using var document = await LoadCombosAsync($"combos?_id={comboId}&limit=1", cancellationToken);
        var row = document.RootElement;
        if (row.ValueKind == JsonValueKind.Null || row.ValueKind == JsonValueKind.Array && row.GetArrayLength() == 0)
            throw new InvalidDataException("Master Duel Meta ne trouve pas ce combo.");
        if (ReadString(row, "_id") != comboId
            || !row.TryGetProperty("deckType", out var deck) || ReadString(deck, "_id") is not { } deckId
            || !MasterDuelMetaParser.IsObjectId(deckId))
            throw new InvalidDataException("Master Duel Meta a renvoyé un combo ou un deck inattendu.");
        return parser.Parse([row], deckId, clock.UtcNow);
    }

    private async Task<ComboCatalog> ImportDeckAsync(string deckId, CancellationToken cancellationToken)
    {
        var rows = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        const int pageSize = 20;
        for (var page = 1; page <= 25; page++)
        {
            using var document = await LoadCombosAsync(
                $"combos/paginated?deckType={deckId}&sort=netVotes&limit={pageSize}&page={page}", cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array || !root.TryGetProperty("total", out var count)
                || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var total) || total is < 0 or > 500)
                throw new InvalidDataException("Réponse Master Duel Meta trop volumineuse ou inattendue.");
            foreach (var row in data.EnumerateArray())
            {
                if (ReadString(row, "_id") is not { } id || !MasterDuelMetaParser.IsObjectId(id) || !seen.Add(id))
                    throw new InvalidDataException("Pagination Master Duel Meta incohérente. Réessayer la synchronisation.");
                rows.Add(row.Clone());
            }
            if (rows.Count >= total) return parser.Parse(rows, deckId, clock.UtcNow);
            if (data.GetArrayLength() == 0) throw new InvalidDataException("Page de combos incomplète.");
        }
        throw new InvalidDataException("Limite de pagination atteinte.");
    }

    private async Task<JsonDocument> LoadCombosAsync(string resource, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await DownloadBoundedAsync($"https://www.masterduelmeta.com/api/v1/{resource}", 8 * 1024 * 1024, cancellationToken);
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 48 });
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } status)
        {
            throw new InvalidDataException($"Master Duel Meta n’a pas fourni les combos (HTTP {(int)status}).", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidDataException("Impossible de joindre Master Duel Meta pour récupérer les combos.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidDataException("Master Duel Meta a mis trop de temps à envoyer les combos.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Master Duel Meta a renvoyé une réponse illisible pour les combos.", ex);
        }
    }

    private static string? ReadString(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

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
