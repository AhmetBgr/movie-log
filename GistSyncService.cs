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
    private const string WatchlistBackupFileName = "watchlist.backup.json";
    private const string WatchlistMetaFileName = "watchlist.meta.json";

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

        var primary = await TryReadItemsFileAsync(gist, WatchlistFileName, settings);
        if (primary.Success)
        {
            return new GistSnapshot
            {
                Items = primary.Items!,
                Hash = ComputeHash(primary.Items!),
                UpdatedAt = gist.UpdatedAt,
                UsedBackup = false,
                SourceFileName = WatchlistFileName,
                WarningMessage = primary.WarningMessage
            };
        }

        var backup = await TryReadItemsFileAsync(gist, WatchlistBackupFileName, settings);
        if (backup.Success)
        {
            return new GistSnapshot
            {
                Items = backup.Items!,
                Hash = ComputeHash(backup.Items!),
                UpdatedAt = gist.UpdatedAt,
                UsedBackup = true,
                SourceFileName = WatchlistBackupFileName,
                WarningMessage = $"Primary gist file is invalid. Loaded backup instead. {primary.ErrorMessage}".Trim()
            };
        }

        if (primary.Exists)
            throw new InvalidOperationException($"Primary and backup gist files are unreadable. {primary.ErrorMessage} {backup.ErrorMessage}".Trim());

        return new GistSnapshot
        {
            Items = new List<WatchlistItem>(),
            Hash = ComputeHash(Array.Empty<WatchlistItem>()),
            UpdatedAt = gist.UpdatedAt,
            UsedBackup = false,
            SourceFileName = WatchlistFileName
        };
    }

    public async Task<GistSaveResult> SaveToGistAsync(List<WatchlistItem> items)
    {
        var settings = await RequireSettingsAsync();
        var existingSnapshot = await TryLoadExistingSnapshotForBackupAsync(settings);
        var newItems = items ?? new List<WatchlistItem>();
        var contentJson = JsonSerializer.Serialize(newItems, JsonOptions);
        var backupJson = existingSnapshot?.RawJson ?? contentJson;
        var meta = new GistMeta
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Hash = ComputeHash(newItems),
            BackupSource = existingSnapshot?.SourceFileName ?? WatchlistFileName
        };

        var payload = new GistPatchRequest
        {
            Files = new Dictionary<string, GistFilePatch>
            {
                [WatchlistFileName] = new GistFilePatch { Content = contentJson },
                [WatchlistBackupFileName] = new GistFilePatch { Content = backupJson },
                [WatchlistMetaFileName] = new GistFilePatch { Content = JsonSerializer.Serialize(meta, JsonOptions) }
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
            Hash = meta.Hash,
            UpdatedAt = meta.UpdatedAt,
            SourceFileName = WatchlistFileName,
            WarningMessage = existingSnapshot?.WarningMessage
        };
    }

    private async Task<GistSnapshot?> TryLoadExistingSnapshotForBackupAsync(GistSettings settings)
    {
        try
        {
            var gist = await FetchGistAsync(settings);
            var primary = await TryReadItemsFileAsync(gist, WatchlistFileName, settings);
            if (primary.Success)
            {
                return new GistSnapshot
                {
                    Items = primary.Items!,
                    Hash = ComputeHash(primary.Items!),
                    UpdatedAt = gist.UpdatedAt,
                    UsedBackup = false,
                    SourceFileName = WatchlistFileName,
                    WarningMessage = primary.WarningMessage,
                    RawJson = primary.RawJson
                };
            }

            var backup = await TryReadItemsFileAsync(gist, WatchlistBackupFileName, settings);
            if (backup.Success)
            {
                return new GistSnapshot
                {
                    Items = backup.Items!,
                    Hash = ComputeHash(backup.Items!),
                    UpdatedAt = gist.UpdatedAt,
                    UsedBackup = true,
                    SourceFileName = WatchlistBackupFileName,
                    WarningMessage = $"Primary gist file is invalid. Preserving backup as the recovery copy. {primary.ErrorMessage}".Trim(),
                    RawJson = backup.RawJson
                };
            }
        }
        catch
        {
            // If we cannot read the previous remote state, we still want to allow a save from local data.
        }

        return null;
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

    private async Task<GistReadAttempt> TryReadItemsFileAsync(GistResponse gist, string fileName, GistSettings settings)
    {
        if (gist.Files == null || !gist.Files.TryGetValue(fileName, out var file))
        {
            return new GistReadAttempt { Exists = false };
        }

        var rawJson = await ReadFileContentAsync(file, settings);
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return new GistReadAttempt
            {
                Exists = true,
                Success = true,
                Items = new List<WatchlistItem>(),
                RawJson = "[]"
            };
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<WatchlistItem>>(rawJson, JsonOptions) ?? new List<WatchlistItem>();
            return new GistReadAttempt
            {
                Exists = true,
                Success = true,
                Items = items,
                WarningMessage = file.Truncated ? $"Loaded {fileName} from raw_url because the gist payload was truncated." : null,
                RawJson = rawJson
            };
        }
        catch (JsonException ex)
        {
            return new GistReadAttempt
            {
                Exists = true,
                Success = false,
                ErrorMessage = $"File '{fileName}' contains invalid JSON: {ex.Message}",
                RawJson = rawJson
            };
        }
    }

    private async Task<string?> ReadFileContentAsync(GistFile file, GistSettings settings)
    {
        if (!file.Truncated && !string.IsNullOrWhiteSpace(file.Content))
            return file.Content;

        if (string.IsNullOrWhiteSpace(file.RawUrl))
            return file.Content;

        using var request = new HttpRequestMessage(HttpMethod.Get, file.RawUrl);
        ApplyGitHubHeaders(request, settings.PersonalAccessToken);

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub gist raw file fetch failed ({(int)response.StatusCode}): {raw}");

        return raw;
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

        [JsonPropertyName("raw_url")]
        public string? RawUrl { get; set; }

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

    private sealed class GistReadAttempt
    {
        public bool Exists { get; set; }
        public bool Success { get; set; }
        public List<WatchlistItem>? Items { get; set; }
        public string? WarningMessage { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RawJson { get; set; }
    }

    private sealed class GistMeta
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public string Hash { get; set; } = "";
        public string BackupSource { get; set; } = WatchlistFileName;
    }
}

public sealed class GistSnapshot
{
    public List<WatchlistItem> Items { get; set; } = new();
    public string Hash { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool UsedBackup { get; set; }
    public string SourceFileName { get; set; } = "watchlist.json";
    public string? WarningMessage { get; set; }
    public string? RawJson { get; set; }

    public string DescribeSource()
        => UsedBackup ? "Loaded backup gist file." : "Loaded primary gist file.";
}

public sealed class GistSaveResult
{
    public string Hash { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string SourceFileName { get; set; } = "watchlist.json";
    public string? WarningMessage { get; set; }

    public string DescribeSource()
        => "Saved primary gist file and refreshed backup.";
}
