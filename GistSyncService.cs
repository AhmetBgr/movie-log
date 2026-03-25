using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
            PersonalAccessToken = settings.PersonalAccessToken?.Trim() ?? ""
        };
        await _storage.SaveAsync(SettingsStorageKey, normalized);
    }

    public async Task ClearSettingsAsync()
        => await _storage.RemoveAsync(SettingsStorageKey);

    public async Task<List<WatchlistItem>> LoadFromGistAsync()
    {
        var settings = await RequireSettingsAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/gists/{Uri.EscapeDataString(settings.GistId)}");
        ApplyGitHubHeaders(request, settings.PersonalAccessToken);

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub gist fetch failed ({(int)response.StatusCode}): {raw}");

        var gist = JsonSerializer.Deserialize<GistResponse>(raw, JsonOptions);
        if (gist?.Files == null || !gist.Files.TryGetValue(WatchlistFileName, out var file))
            return new List<WatchlistItem>();

        if (string.IsNullOrWhiteSpace(file.Content))
            return new List<WatchlistItem>();

        return JsonSerializer.Deserialize<List<WatchlistItem>>(file.Content, JsonOptions) ?? new List<WatchlistItem>();
    }

    public async Task SaveToGistAsync(List<WatchlistItem> items)
    {
        var settings = await RequireSettingsAsync();
        var contentJson = JsonSerializer.Serialize(items ?? new List<WatchlistItem>(), JsonOptions);

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

    private sealed class GistResponse
    {
        public Dictionary<string, GistFile>? Files { get; set; }
    }

    private sealed class GistFile
    {
        public string? Content { get; set; }
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
