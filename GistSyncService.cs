using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyPrivateWatchlist.Models;

namespace MyPrivateWatchlist.Services;

public class GistSyncService
{
    private const string SettingsStorageKey = "gist_settings";
    private const string WatchlistFileName = "watchlist.json";

    private readonly HttpClient _http;
    private readonly LocalStorageService _storage;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public GistSyncService(HttpClient http, LocalStorageService storage)
    {
        _http = http;
        _storage = storage;
    }

    public async Task<GistSettings> GetSettingsAsync()
        => await _storage.GetAsync<GistSettings>(SettingsStorageKey) ?? new GistSettings();

    public async Task SaveSettingsAsync(GistSettings settings)
    {
        settings ??= new GistSettings();
        var normalized = new GistSettings
        {
            GistId = settings.GistId?.Trim() ?? "",
            PersonalAccessToken = settings.PersonalAccessToken?.Trim() ?? "",
            AutoSyncMode = GistAutoSyncMode.Disabled,
            AutoPullIntervalMinutes = Math.Max(1, settings.AutoPullIntervalMinutes),
            AutoSyncPaused = true
        };
        await _storage.SaveAsync(SettingsStorageKey, normalized);
    }

    public async Task ClearSettingsAsync()
        => await _storage.RemoveAsync(SettingsStorageKey);

    public async Task<List<WatchlistItem>> LoadFromGistAsync()
        => (await LoadSnapshotAsync()).Items;

    public async Task<GistSnapshot> LoadSnapshotAsync()
    {
        var settings = await RequireSettingsAsync();
        var gist = await FetchGistAsync(settings);

        if (gist.Files == null || !gist.Files.TryGetValue(WatchlistFileName, out var file))
        {
            return new GistSnapshot
            {
                Items = new List<WatchlistItem>(),
                Hash = ComputeHash(Array.Empty<WatchlistItem>()),
                UpdatedAt = gist.UpdatedAt,
                SourceFileName = WatchlistFileName
            };
        }

        if (file.Truncated)
            throw new InvalidOperationException($"'{WatchlistFileName}' is too large for this browser sync path. Please reduce the gist size or replace it with a smaller valid JSON file.");

        if (string.IsNullOrWhiteSpace(file.Content))
        {
            return new GistSnapshot
            {
                Items = new List<WatchlistItem>(),
                Hash = ComputeHash(Array.Empty<WatchlistItem>()),
                UpdatedAt = gist.UpdatedAt,
                SourceFileName = WatchlistFileName
            };
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<WatchlistItem>>(file.Content, JsonOptions) ?? new List<WatchlistItem>();
            return new GistSnapshot
            {
                Items = items,
                Hash = ComputeHash(items),
                UpdatedAt = gist.UpdatedAt,
                SourceFileName = WatchlistFileName
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"File '{WatchlistFileName}' contains invalid JSON: {ex.Message}", ex);
        }
    }

    public async Task<GistSaveResult> SaveToGistAsync(List<WatchlistItem> items)
    {
        var settings = await RequireSettingsAsync();
        var newItems = items ?? new List<WatchlistItem>();
        var contentJson = JsonSerializer.Serialize(newItems, JsonOptions);

        var payload = new GistPatchRequest
        {
            Files = new Dictionary<string, GistFilePatch>
            {
                [WatchlistFileName] = new GistFilePatch { Content = contentJson }
            }
        };

        var requestBody = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"https://api.github.com/gists/{Uri.EscapeDataString(settings.GistId)}")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
        };
        ApplyGitHubHeaders(request, settings.PersonalAccessToken);

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub gist save failed ({(int)response.StatusCode}): {raw}");

        return new GistSaveResult
        {
            Hash = ComputeHash(newItems),
            UpdatedAt = DateTimeOffset.UtcNow,
            SourceFileName = WatchlistFileName
        };
    }

    private async Task<GistResponse> FetchGistAsync(GistSettings settings)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/gists/{Uri.EscapeDataString(settings.GistId)}");
        ApplyGitHubHeaders(request, settings.PersonalAccessToken);

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub gist fetch failed ({(int)response.StatusCode}): {raw}");

        return JsonSerializer.Deserialize<GistResponse>(raw, JsonOptions)
               ?? throw new InvalidOperationException("GitHub gist response could not be parsed.");
    }

    private async Task<GistSettings> RequireSettingsAsync()
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.GistId) || string.IsNullOrWhiteSpace(settings.PersonalAccessToken))
            throw new InvalidOperationException("Gist settings are missing. Please provide both Gist ID and Personal Access Token.");
        return settings;
    }

    private static void ApplyGitHubHeaders(HttpRequestMessage request, string personalAccessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
        request.Headers.UserAgent.ParseAdd("MovieLog/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static string ComputeHash(IEnumerable<WatchlistItem> items)
    {
        var normalized = items
            .OrderBy(i => i.ImdbId, StringComparer.OrdinalIgnoreCase)
            .Select(i => new
            {
                i.ImdbId,
                i.Title,
                i.TitleType,
                i.Year,
                i.Genres,
                i.Director,
                i.OriginalTitle,
                i.ParsedYear,
                Status = (int)i.Status,
                i.CurrentSeason,
                i.CurrentEpisode,
                i.DateAdded,
                i.UserRating,
                i.Rating20,
                i.Overview,
                i.PosterPath,
                i.VoteAverage
            })
            .ToList();

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private sealed class GistResponse
    {
        [JsonPropertyName("files")]
        public Dictionary<string, GistFile>? Files { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    private sealed class GistFile
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }
    }

    private sealed class GistPatchRequest
    {
        public Dictionary<string, GistFilePatch> Files { get; set; } = new();
    }

    private sealed class GistFilePatch
    {
        public string Content { get; set; } = "";
    }
}

public sealed class GistSnapshot
{
    public List<WatchlistItem> Items { get; set; } = new();
    public string Hash { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string SourceFileName { get; set; } = "watchlist.json";
    public string? WarningMessage { get; set; }
    public bool UsedBackup => false;

    public string DescribeSource()
        => "Loaded primary gist file.";
}

public sealed class GistSaveResult
{
    public string Hash { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string SourceFileName { get; set; } = "watchlist.json";
    public string? WarningMessage { get; set; }

    public string DescribeSource()
        => "Saved primary gist file.";
}
