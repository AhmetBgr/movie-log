using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyPrivateWatchlist.Models;

namespace MyPrivateWatchlist.Services;

internal static class WatchlistSyncData
{
    private const string PayloadFormatV1 = "watchlist-slim-gzip-base64-v1";
    private const string PayloadFormatV2 = "watchlist-slim-gzip-base64-v2";

    private static readonly JsonSerializerOptions CompactJsonOptions = new(JsonSerializerDefaults.Web);

    public static string ComputeHash(IEnumerable<WatchlistItem> items, IEnumerable<CustomCollection> collections)
    {
        var normalizedItems = CreateSlimItems(items);
        var normalizedCollections = collections?.OrderBy(c => c.Id).ToList() ?? new List<CustomCollection>();
        var payload = new SyncDataV2 { Items = normalizedItems, Collections = normalizedCollections };
        
        var json = JsonSerializer.Serialize(payload, CompactJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    public static string SerializeCompressed(IEnumerable<WatchlistItem> items, IEnumerable<CustomCollection> collections)
    {
        var normalizedItems = CreateSlimItems(items);
        var normalizedCollections = collections?.OrderBy(c => c.Id).ToList() ?? new List<CustomCollection>();
        var payload = new SyncDataV2 { Items = normalizedItems, Collections = normalizedCollections };
        
        var json = JsonSerializer.Serialize(payload, CompactJsonOptions);
        var syncPayload = new GistSyncPayload
        {
            Format = PayloadFormatV2,
            Data = CompressToBase64(json)
        };

        return JsonSerializer.Serialize(syncPayload, CompactJsonOptions);
    }

    public static (List<WatchlistItem> Items, List<CustomCollection> Collections) Deserialize(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return (new List<WatchlistItem>(), new List<CustomCollection>());

        if (TryDeserializeCompressedPayload(content, out var compressedItems, out var compressedCollections))
            return (compressedItems, compressedCollections);

        if (TryDeserializeSlimItems(content, out var slimItems))
            return (slimItems, new List<CustomCollection>());

        var oldItems = JsonSerializer.Deserialize<List<WatchlistItem>>(content, CompactJsonOptions) ?? new List<WatchlistItem>();
        return (oldItems, new List<CustomCollection>());
    }

    private static bool TryDeserializeCompressedPayload(string content, out List<WatchlistItem> items, out List<CustomCollection> collections)
    {
        items = new List<WatchlistItem>();
        collections = new List<CustomCollection>();

        try
        {
            var payload = JsonSerializer.Deserialize<GistSyncPayload>(content, CompactJsonOptions);
            var data = payload?.Data;
            if (string.IsNullOrWhiteSpace(data))
                return false;

            var decompressed = DecompressFromBase64(data);
            
            if (string.Equals(payload?.Format, PayloadFormatV2, StringComparison.Ordinal))
            {
                var v2 = JsonSerializer.Deserialize<SyncDataV2>(decompressed, CompactJsonOptions);
                if (v2 != null)
                {
                    items = v2.Items.Select(ToWatchlistItem).ToList();
                    collections = v2.Collections ?? new List<CustomCollection>();
                    return true;
                }
            }
            else if (string.Equals(payload?.Format, PayloadFormatV1, StringComparison.Ordinal))
            {
                if (TryDeserializeSlimItems(decompressed, out var slimItems))
                {
                    items = slimItems;
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeserializeSlimItems(string content, out List<WatchlistItem> items)
    {
        items = new List<WatchlistItem>();

        try
        {
            var slimItems = JsonSerializer.Deserialize<List<GistWatchlistItem>>(content, CompactJsonOptions);
            if (slimItems == null)
                return false;

            items = slimItems.Select(ToWatchlistItem).ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<GistWatchlistItem> CreateSlimItems(IEnumerable<WatchlistItem> items)
        => (items ?? Enumerable.Empty<WatchlistItem>())
            .OrderBy(i => i.ImdbId, StringComparer.OrdinalIgnoreCase)
            .Select(item => new GistWatchlistItem
            {
                ImdbId = item.ImdbId,
                Title = item.Title,
                TitleType = item.TitleType,
                Year = item.Year,
                Status = item.Status,
                DateAdded = item.DateAdded,
                UserRating = item.UserRating,
                Rating20 = item.Rating20,
                CurrentSeason = item.CurrentSeason,
                CurrentEpisode = item.CurrentEpisode,
                Genres = item.Genres
            })
            .ToList();

    private static WatchlistItem ToWatchlistItem(GistWatchlistItem item)
    {
        var parsedYear = 0;
        if (!string.IsNullOrWhiteSpace(item.Year))
        {
            var yearDigits = new string(item.Year.TakeWhile(char.IsDigit).ToArray());
            int.TryParse(yearDigits, out parsedYear);
        }

        var userRating = item.UserRating;
        var rating20 = item.Rating20;
        if (!rating20.HasValue && userRating.HasValue)
            rating20 = userRating.Value * 10;

        return new WatchlistItem
        {
            ImdbId = item.ImdbId ?? "",
            Title = item.Title ?? "",
            TitleType = item.TitleType ?? "",
            Year = item.Year ?? "",
            ParsedYear = parsedYear,
            Status = item.Status,
            DateAdded = item.DateAdded == default ? DateTime.Now : item.DateAdded,
            UserRating = userRating,
            Rating20 = rating20,
            CurrentSeason = item.CurrentSeason,
            CurrentEpisode = item.CurrentEpisode,
            Genres = item.Genres ?? ""
        };
    }

    private static string CompressToBase64(string content)
    {
        var inputBytes = Encoding.UTF8.GetBytes(content);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(inputBytes, 0, inputBytes.Length);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    private static string DecompressFromBase64(string base64)
    {
        var compressedBytes = Convert.FromBase64String(base64);
        using var input = new MemoryStream(compressedBytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class GistSyncPayload
    {
        public string Format { get; set; } = "";
        public string Data { get; set; } = "";
    }

    private sealed class SyncDataV2
    {
        public List<GistWatchlistItem> Items { get; set; } = new();
        public List<CustomCollection> Collections { get; set; } = new();
    }

    private sealed class GistWatchlistItem
    {
        public string? ImdbId { get; set; }
        public string? Title { get; set; }
        public string? TitleType { get; set; }
        public string? Year { get; set; }
        public WatchlistStatus Status { get; set; } = WatchlistStatus.Pending;
        public DateTime DateAdded { get; set; }
        public int? UserRating { get; set; }
        public int? Rating20 { get; set; }
        public int? CurrentSeason { get; set; }
        public int? CurrentEpisode { get; set; }
        public string? Genres { get; set; }
    }
}
