using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyPrivateWatchlist.Models;

namespace MyPrivateWatchlist.Services;

public class GistSyncService
{
    private const string SettingsStorageKey = "gist_settings";
    private const string WatchlistFileName = "watchlist.json";
    private const string BackupIndexFileName = "backup-index.json";
    private const string BackupFilePrefix = "watchlist-backup-";
    private const int MaxBackupSnapshots = 20;

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

        return LoadSnapshotFromResponse(gist);
    }

    public async Task<IReadOnlyList<GistBackupInfo>> GetBackupsAsync()
    {
        var settings = await RequireSettingsAsync();
        var gist = await FetchGistAsync(settings);
        return LoadBackupEntries(gist.Files)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public async Task<GistSnapshot> LoadBackupSnapshotAsync(string backupFileName)
    {
        if (string.IsNullOrWhiteSpace(backupFileName))
            throw new ArgumentException("Backup file name is required.", nameof(backupFileName));

        var settings = await RequireSettingsAsync();
        var gist = await FetchGistAsync(settings);
        if (gist.Files == null || !gist.Files.TryGetValue(backupFileName, out var file))
            throw new InvalidOperationException($"Backup file '{backupFileName}' was not found in the gist.");

        return CreateSnapshotFromFile(file, gist.UpdatedAt, backupFileName, usedBackup: true, warningMessage: null);
    }

    public async Task<GistSaveResult> RestoreBackupToPrimaryAsync(string backupFileName)
    {
        if (string.IsNullOrWhiteSpace(backupFileName))
            throw new ArgumentException("Backup file name is required.", nameof(backupFileName));

        var settings = await RequireSettingsAsync();
        var gist = await FetchGistAsync(settings);
        if (gist.Files == null || !gist.Files.TryGetValue(backupFileName, out var backupFile))
            throw new InvalidOperationException($"Backup file '{backupFileName}' was not found in the gist.");

        var restoreSnapshot = CreateSnapshotFromFile(backupFile, gist.UpdatedAt, backupFileName, usedBackup: true, warningMessage: null);
        GistBackupDraft? safetyBackup = null;

        if (gist.Files.TryGetValue(WatchlistFileName, out var currentPrimary) &&
            TryParseWatchlistFile(currentPrimary, out var currentItems, out var currentCollections, out _))
        {
            var currentHash = WatchlistSyncData.ComputeHash(currentItems, currentCollections);
            if (!string.Equals(currentHash, restoreSnapshot.Hash, StringComparison.Ordinal))
            {
                safetyBackup = CreateBackupDraft(currentItems, currentCollections, "before-restore");
            }
        }

        return await SaveItemsToExistingGistAsync(settings, gist, restoreSnapshot.Items, restoreSnapshot.Collections, "restore", safetyBackup);
    }

    public async Task<GistSaveResult> SaveToGistAsync(List<WatchlistItem> items, List<CustomCollection> collections, string reason = "manual")
    {
        var settings = await RequireSettingsAsync();
        var gist = await FetchGistAsync(settings);
        return await SaveItemsToExistingGistAsync(settings, gist, items ?? new List<WatchlistItem>(), collections ?? new List<CustomCollection>(), reason, safetyBackup: null);
    }

    public async Task<GistSaveResult> CreateBackupAsync(List<WatchlistItem> items, List<CustomCollection> collections, string reason)
    {
        var settings = await RequireSettingsAsync();
        var gist = await FetchGistAsync(settings);
        var backupDraft = CreateBackupDraft(items ?? new List<WatchlistItem>(), collections ?? new List<CustomCollection>(), reason);

        var payload = BuildBackupOnlyPatch(gist, backupDraft);
        var updatedAt = await PatchGistAsync(settings, payload);

        return new GistSaveResult
        {
            Hash = backupDraft.Hash,
            UpdatedAt = updatedAt,
            SourceFileName = backupDraft.FileName,
            CreatedBackupFileName = backupDraft.FileName,
            BackupCount = LoadBackupEntriesAfterApply(gist, backupDraft).Count,
            WarningMessage = null
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

    private GistSnapshot LoadSnapshotFromResponse(GistResponse gist)
    {
        if (gist.Files != null && gist.Files.TryGetValue(WatchlistFileName, out var primaryFile))
        {
            try
            {
                return CreateSnapshotFromFile(primaryFile, gist.UpdatedAt, WatchlistFileName, usedBackup: false, warningMessage: null);
            }
            catch (InvalidOperationException primaryEx)
            {
                var fallback = TryLoadLatestBackupSnapshot(gist, primaryEx.Message);
                if (fallback != null)
                    return fallback;

                throw;
            }
        }

        var backupFallback = TryLoadLatestBackupSnapshot(gist, $"Primary file '{WatchlistFileName}' is missing. Loaded the newest backup instead.");
        if (backupFallback != null)
            return backupFallback;

        return new GistSnapshot
        {
            Items = new List<WatchlistItem>(),
            Collections = new List<CustomCollection>(),
            Hash = WatchlistSyncData.ComputeHash(Array.Empty<WatchlistItem>(), Array.Empty<CustomCollection>()),
            UpdatedAt = gist.UpdatedAt,
            SourceFileName = WatchlistFileName
        };
    }

    private GistSnapshot? TryLoadLatestBackupSnapshot(GistResponse gist, string warningMessage)
    {
        if (gist.Files == null)
            return null;

        foreach (var backup in LoadBackupEntries(gist.Files).OrderByDescending(x => x.CreatedAt))
        {
            if (!gist.Files.TryGetValue(backup.FileName, out var file))
                continue;

            try
            {
                return CreateSnapshotFromFile(file, gist.UpdatedAt, backup.FileName, usedBackup: true, warningMessage: warningMessage);
            }
            catch
            {
                // Try the next backup entry.
            }
        }

        return null;
    }

    private static GistSnapshot CreateSnapshotFromFile(GistFile file, DateTimeOffset? updatedAt, string sourceFileName, bool usedBackup, string? warningMessage)
    {
        if (file.Truncated)
            throw new InvalidOperationException($"'{sourceFileName}' is too large for this browser sync path. Please reduce the gist size or replace it with a smaller valid JSON file.");

        if (string.IsNullOrWhiteSpace(file.Content))
        {
            return new GistSnapshot
            {
                Items = new List<WatchlistItem>(),
                Collections = new List<CustomCollection>(),
                Hash = WatchlistSyncData.ComputeHash(Array.Empty<WatchlistItem>(), Array.Empty<CustomCollection>()),
                UpdatedAt = updatedAt,
                SourceFileName = sourceFileName,
                UsedBackup = usedBackup,
                WarningMessage = warningMessage
            };
        }

        try
        {
            var parsed = WatchlistSyncData.Deserialize(file.Content);
            return new GistSnapshot
            {
                Items = parsed.Items,
                Collections = parsed.Collections,
                Hash = WatchlistSyncData.ComputeHash(parsed.Items, parsed.Collections),
                UpdatedAt = updatedAt,
                SourceFileName = sourceFileName,
                UsedBackup = usedBackup,
                WarningMessage = warningMessage
            };
        }
        catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException)
        {
            throw new InvalidOperationException($"File '{sourceFileName}' contains invalid JSON: {ex.Message}", ex);
        }
    }

    private static bool TryParseWatchlistFile(GistFile file, out List<WatchlistItem> items, out List<CustomCollection> collections, out string? error)
    {
        items = new List<WatchlistItem>();
        collections = new List<CustomCollection>();
        error = null;

        if (file.Truncated)
        {
            error = "File is truncated.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(file.Content))
            return true;

        try
        {
            var parsed = WatchlistSyncData.Deserialize(file.Content);
            items = parsed.Items;
            collections = parsed.Collections;
            return true;
        }
        catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is FormatException)
        {
            error = ex.Message;
            return false;
        }
    }

    private async Task<GistSaveResult> SaveItemsToExistingGistAsync(
        GistSettings settings,
        GistResponse gist,
        List<WatchlistItem> items,
        List<CustomCollection> collections,
        string reason,
        GistBackupDraft? safetyBackup)
    {
        var newItems = items ?? new List<WatchlistItem>();
        var newCollections = collections ?? new List<CustomCollection>();
        var primaryContent = WatchlistSyncData.SerializeCompressed(newItems, newCollections);
        var primaryHash = WatchlistSyncData.ComputeHash(newItems, newCollections);
        var primaryBackup = CreateBackupDraft(newItems, newCollections, reason);

        var payload = BuildPrimaryAndBackupPatch(gist, primaryContent, primaryBackup, safetyBackup);
        var updatedAt = await PatchGistAsync(settings, payload);
        var updatedBackupCount = LoadBackupEntriesAfterApply(gist, primaryBackup, safetyBackup).Count;

        return new GistSaveResult
        {
            Hash = primaryHash,
            UpdatedAt = updatedAt,
            SourceFileName = WatchlistFileName,
            CreatedBackupFileName = primaryBackup.FileName,
            BackupCount = updatedBackupCount
        };
    }

    private GistPatchRequest BuildPrimaryAndBackupPatch(
        GistResponse gist,
        string primaryContent,
        GistBackupDraft primaryBackup,
        GistBackupDraft? safetyBackup)
    {
        var nextEntries = LoadBackupEntries(gist.Files);
        if (safetyBackup != null)
            nextEntries.Insert(0, safetyBackup.Info);

        nextEntries.Insert(0, primaryBackup.Info);

        var files = new Dictionary<string, GistFilePatch?>(StringComparer.Ordinal)
        {
            [WatchlistFileName] = new GistFilePatch { Content = primaryContent },
            [primaryBackup.FileName] = new GistFilePatch { Content = primaryBackup.Content },
            [BackupIndexFileName] = new GistFilePatch { Content = SerializeBackupIndex(ApplyRetention(nextEntries)) }
        };

        if (safetyBackup != null)
            files[safetyBackup.FileName] = new GistFilePatch { Content = safetyBackup.Content };

        foreach (var fileName in GetBackupFilesToDelete(nextEntries))
            files[fileName] = null;

        return new GistPatchRequest { Files = files };
    }

    private GistPatchRequest BuildBackupOnlyPatch(GistResponse gist, GistBackupDraft backup)
    {
        var nextEntries = LoadBackupEntries(gist.Files);
        nextEntries.Insert(0, backup.Info);

        var files = new Dictionary<string, GistFilePatch?>(StringComparer.Ordinal)
        {
            [backup.FileName] = new GistFilePatch { Content = backup.Content },
            [BackupIndexFileName] = new GistFilePatch { Content = SerializeBackupIndex(ApplyRetention(nextEntries)) }
        };

        foreach (var fileName in GetBackupFilesToDelete(nextEntries))
            files[fileName] = null;

        return new GistPatchRequest { Files = files };
    }

    private async Task<DateTimeOffset?> PatchGistAsync(GistSettings settings, GistPatchRequest payload)
    {
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

        var updated = JsonSerializer.Deserialize<GistResponse>(raw, JsonOptions);
        return updated?.UpdatedAt ?? DateTimeOffset.UtcNow;
    }

    private static GistBackupDraft CreateBackupDraft(List<WatchlistItem> items, List<CustomCollection> collections, string reason)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim().ToLowerInvariant();
        var safeReason = new string(normalizedReason.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (string.IsNullOrWhiteSpace(safeReason))
            safeReason = "manual";
        var now = DateTimeOffset.UtcNow;
        var fileName = $"{BackupFilePrefix}{now:yyyyMMdd-HHmmssfff}-{safeReason}.json";
        var content = WatchlistSyncData.SerializeCompressed(items, collections);
        var hash = WatchlistSyncData.ComputeHash(items, collections);

        return new GistBackupDraft
        {
            FileName = fileName,
            Content = content,
            Hash = hash,
            Info = new GistBackupInfo
            {
                FileName = fileName,
                CreatedAt = now,
                Hash = hash,
                ItemCount = items.Count,
                Reason = normalizedReason
            }
        };
    }

    private static List<GistBackupInfo> LoadBackupEntries(Dictionary<string, GistFile>? files)
    {
        if (files == null || !files.TryGetValue(BackupIndexFileName, out var indexFile) || string.IsNullOrWhiteSpace(indexFile.Content))
            return InferBackupEntries(files);

        if (indexFile.Truncated)
            return InferBackupEntries(files);

        try
        {
            var index = JsonSerializer.Deserialize<GistBackupIndex>(indexFile.Content, JsonOptions);
            return (index?.Backups ?? new List<GistBackupInfo>())
                .Where(x => !string.IsNullOrWhiteSpace(x.FileName))
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }
        catch
        {
            return InferBackupEntries(files);
        }
    }

    private static List<GistBackupInfo> InferBackupEntries(Dictionary<string, GistFile>? files)
    {
        if (files == null)
            return new List<GistBackupInfo>();

        return files.Keys
            .Where(x => x.StartsWith(BackupFilePrefix, StringComparison.OrdinalIgnoreCase) &&
                        x.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(fileName => new GistBackupInfo
            {
                FileName = fileName,
                CreatedAt = TryParseBackupTimestamp(fileName),
                Reason = "unknown"
            })
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    private static DateTimeOffset TryParseBackupTimestamp(string fileName)
    {
        var trimmed = fileName
            .Replace(BackupFilePrefix, "", StringComparison.OrdinalIgnoreCase)
            .Replace(".json", "", StringComparison.OrdinalIgnoreCase);
        var parts = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return DateTimeOffset.MinValue;

        var timestamp = $"{parts[0]}-{parts[1]}";

        return DateTimeOffset.TryParseExact(
            timestamp,
            "yyyyMMdd-HHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static List<GistBackupInfo> ApplyRetention(List<GistBackupInfo> entries)
        => entries
            .Where(x => !string.IsNullOrWhiteSpace(x.FileName))
            .OrderByDescending(x => x.CreatedAt)
            .Take(MaxBackupSnapshots)
            .ToList();

    private static IEnumerable<string> GetBackupFilesToDelete(List<GistBackupInfo> nextEntries)
    {
        var retained = ApplyRetention(nextEntries)
            .Select(x => x.FileName)
            .ToHashSet(StringComparer.Ordinal);

        return nextEntries
            .Where(x => !retained.Contains(x.FileName))
            .Select(x => x.FileName)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string SerializeBackupIndex(List<GistBackupInfo> entries)
        => JsonSerializer.Serialize(new GistBackupIndex { Backups = ApplyRetention(entries) }, JsonOptions);

    private static List<GistBackupInfo> LoadBackupEntriesAfterApply(
        GistResponse gist,
        GistBackupDraft primaryBackup,
        GistBackupDraft? safetyBackup = null)
    {
        var entries = LoadBackupEntries(gist.Files);
        if (safetyBackup != null)
            entries.Insert(0, safetyBackup.Info);
        entries.Insert(0, primaryBackup.Info);
        return ApplyRetention(entries);
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
        public Dictionary<string, GistFilePatch?> Files { get; set; } = new();
    }

    private sealed class GistFilePatch
    {
        public string Content { get; set; } = "";
    }

    private sealed class GistBackupDraft
    {
        public string FileName { get; set; } = "";
        public string Content { get; set; } = "";
        public string Hash { get; set; } = "";
        public GistBackupInfo Info { get; set; } = new();
    }

    private sealed class GistBackupIndex
    {
        public List<GistBackupInfo> Backups { get; set; } = new();
    }
}

public sealed class GistSnapshot
{
    public List<WatchlistItem> Items { get; set; } = new();
    public List<CustomCollection> Collections { get; set; } = new();
    public string Hash { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string SourceFileName { get; set; } = "watchlist.json";
    public string? WarningMessage { get; set; }
    public bool UsedBackup { get; set; }

    public string DescribeSource()
        => UsedBackup
            ? $"Loaded backup gist file '{SourceFileName}'."
            : "Loaded primary gist file.";
}

public sealed class GistSaveResult
{
    public string Hash { get; set; } = "";
    public DateTimeOffset? UpdatedAt { get; set; }
    public string SourceFileName { get; set; } = "watchlist.json";
    public string? CreatedBackupFileName { get; set; }
    public int BackupCount { get; set; }
    public string? WarningMessage { get; set; }

    public string DescribeSource()
        => string.IsNullOrWhiteSpace(CreatedBackupFileName)
            ? $"Saved '{SourceFileName}'."
            : $"Saved '{SourceFileName}' and backup '{CreatedBackupFileName}'.";
}

public sealed class GistBackupInfo
{
    public string FileName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Hash { get; set; } = "";
    public int ItemCount { get; set; }
    public string Reason { get; set; } = "manual";
}
