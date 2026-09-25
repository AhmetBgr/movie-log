using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyPrivateWatchlist.Models;

namespace MyPrivateWatchlist.Services;

/// <summary>
/// Thin wrapper over the Supabase Auth (GoTrue) and Data (PostgREST) HTTP APIs.
/// Talks to a single <c>library_records</c> table; see <c>supabase/schema.sql</c>.
/// </summary>
public class SupabaseApi
{
    private const string SettingsStorageKey = "supabase_settings";
    private const string SessionStorageKey = "supabase_session";
    private const string TableName = "library_records";
    private const int PageSize = 1000;      // PostgREST's default max-rows
    private const int FetchChunkSize = 100; // keys per "in.(...)" filter
    private const int UpsertChunkSize = 500;

    private readonly HttpClient _http;
    private readonly LocalStorageService _storage;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public SupabaseApi(HttpClient http, LocalStorageService storage)
    {
        _http = http;
        _storage = storage;
    }

    // ── Settings & session ───────────────────────────────────────────────

    public async Task<SupabaseSettings> GetSettingsAsync()
        => await _storage.GetAsync<SupabaseSettings>(SettingsStorageKey) ?? new SupabaseSettings();

    public async Task SaveSettingsAsync(SupabaseSettings settings)
    {
        var normalized = new SupabaseSettings
        {
            ProjectUrl = (settings.ProjectUrl ?? "").Trim().TrimEnd('/'),
            AnonKey = (settings.AnonKey ?? "").Trim(),
            Email = (settings.Email ?? "").Trim(),
            AutoSync = settings.AutoSync
        };
        await _storage.SaveAsync(SettingsStorageKey, normalized);
    }

    public async Task<SupabaseSession?> GetStoredSessionAsync()
    {
        var session = await _storage.GetAsync<SupabaseSession>(SessionStorageKey);
        return string.IsNullOrWhiteSpace(session?.RefreshToken) ? null : session;
    }

    public async Task<SupabaseSession> SignInAsync(string email, string password)
    {
        var settings = await RequireSettingsAsync();
        var response = await SendAuthAsync(settings, "token?grant_type=password", new { email, password });
        var session = await SaveSessionAsync(response);
        return session;
    }

    public async Task SignOutAsync()
    {
        var settings = await GetSettingsAsync();
        var session = await GetStoredSessionAsync();
        await _storage.RemoveAsync(SessionStorageKey);

        if (session == null || !HasSettings(settings)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.ProjectUrl}/auth/v1/logout");
            ApplyHeaders(request, settings, session.AccessToken);
            using var _ = await _http.SendAsync(request);
        }
        catch
        {
            // The local session is already gone; a failed server-side logout is harmless.
        }
    }

    /// <summary>Returns a session with a usable access token, refreshing it when it is about to expire.</summary>
    public async Task<SupabaseSession> GetValidSessionAsync()
    {
        var session = await GetStoredSessionAsync()
                      ?? throw new InvalidOperationException("Not signed in to Supabase.");

        if (session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return session;

        await _refreshGate.WaitAsync();
        try
        {
            // Another caller may have refreshed while we waited.
            session = await GetStoredSessionAsync()
                      ?? throw new InvalidOperationException("Not signed in to Supabase.");
            if (session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return session;

            var settings = await RequireSettingsAsync();
            try
            {
                var response = await SendAuthAsync(settings, "token?grant_type=refresh_token", new { refresh_token = session.RefreshToken });
                return await SaveSessionAsync(response);
            }
            catch (SupabaseAuthException)
            {
                await _storage.RemoveAsync(SessionStorageKey);
                throw new InvalidOperationException("Your Supabase session has expired. Please sign in again.");
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    // ── Records ──────────────────────────────────────────────────────────

    /// <summary>Fetches kind/key/hash/deleted for every record (no payloads).</summary>
    public async Task<List<SupabaseRecordHeader>> FetchRecordHeadersAsync()
    {
        var (settings, session) = await RequireAuthAsync();
        var result = new List<SupabaseRecordHeader>();

        for (var offset = 0; ; offset += PageSize)
        {
            var url = $"{settings.ProjectUrl}/rest/v1/{TableName}?select=kind,key,hash,deleted,updated_at" +
                      $"&order=kind.asc,key.asc&offset={offset}&limit={PageSize}";
            var page = await GetJsonAsync<List<SupabaseRecordHeader>>(settings, session, url);
            result.AddRange(page);
            if (page.Count < PageSize) break;
        }

        return result;
    }

    /// <summary>Returns the newest updated_at across all records (null when there are none). One tiny row, cheap to poll.</summary>
    public async Task<DateTimeOffset?> FetchLatestUpdateAsync()
    {
        var (settings, session) = await RequireAuthAsync();
        var url = $"{settings.ProjectUrl}/rest/v1/{TableName}?select=updated_at&order=updated_at.desc&limit=1";
        var rows = await GetJsonAsync<List<SupabaseRecordHeader>>(settings, session, url);
        return rows.Count > 0 ? rows[0].UpdatedAt : null;
    }

    /// <summary>Fetches full records (including data) for the given keys of one kind.</summary>
    public async Task<List<SupabaseRecord>> FetchRecordsAsync(string kind, IEnumerable<string> keys)
    {
        var (settings, session) = await RequireAuthAsync();
        var result = new List<SupabaseRecord>();

        foreach (var chunk in keys.Distinct(StringComparer.Ordinal).Chunk(FetchChunkSize))
        {
            var inList = string.Join(",", chunk.Select(k => "\"" + k.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));
            var url = $"{settings.ProjectUrl}/rest/v1/{TableName}?select=kind,key,hash,deleted,updated_at,data" +
                      $"&kind=eq.{Uri.EscapeDataString(kind)}&key=in.({Uri.EscapeDataString(inList)})";
            result.AddRange(await GetJsonAsync<List<SupabaseRecord>>(settings, session, url));
        }

        return result;
    }

    public async Task UpsertRecordsAsync(IReadOnlyCollection<SupabaseRecordWrite> records)
    {
        if (records.Count == 0) return;
        var (settings, session) = await RequireAuthAsync();

        foreach (var chunk in records.Chunk(UpsertChunkSize))
        {
            foreach (var record in chunk)
                record.UserId = session.UserId;

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.ProjectUrl}/rest/v1/{TableName}?on_conflict=user_id,kind,key")
            {
                Content = new StringContent(JsonSerializer.Serialize(chunk, JsonOptions), Encoding.UTF8, "application/json")
            };
            ApplyHeaders(request, settings, session.AccessToken);
            request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Supabase save failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public static bool HasSettings(SupabaseSettings settings)
        => !string.IsNullOrWhiteSpace(settings.ProjectUrl) && !string.IsNullOrWhiteSpace(settings.AnonKey);

    private async Task<SupabaseSettings> RequireSettingsAsync()
    {
        var settings = await GetSettingsAsync();
        if (!HasSettings(settings))
            throw new InvalidOperationException("Supabase settings are missing. Please provide the project URL and anon key.");
        if (!settings.ProjectUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Supabase project URL must start with https://");
        return settings;
    }

    private async Task<(SupabaseSettings Settings, SupabaseSession Session)> RequireAuthAsync()
    {
        var settings = await RequireSettingsAsync();
        var session = await GetValidSessionAsync();
        return (settings, session);
    }

    private async Task<T> GetJsonAsync<T>(SupabaseSettings settings, SupabaseSession session, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, settings, session.AccessToken);

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Supabase fetch failed ({(int)response.StatusCode}): {raw}");

        return JsonSerializer.Deserialize<T>(raw, JsonOptions)
               ?? throw new InvalidOperationException("Supabase response could not be parsed.");
    }

    private async Task<AuthResponse> SendAuthAsync(SupabaseSettings settings, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.ProjectUrl}/auth/v1/{path}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        ApplyHeaders(request, settings, accessToken: null);

        using var response = await _http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var message = TryReadAuthError(raw) ?? raw;
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                throw new SupabaseAuthException($"Supabase sign-in failed: {message}");
            throw new InvalidOperationException($"Supabase auth request failed ({(int)response.StatusCode}): {message}");
        }

        return JsonSerializer.Deserialize<AuthResponse>(raw, JsonOptions)
               ?? throw new InvalidOperationException("Supabase auth response could not be parsed.");
    }

    private async Task<SupabaseSession> SaveSessionAsync(AuthResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.AccessToken) || string.IsNullOrWhiteSpace(response.RefreshToken) || response.User == null)
            throw new InvalidOperationException("Supabase returned an incomplete session.");

        var session = new SupabaseSession
        {
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            ExpiresAt = response.ExpiresAt > 0
                ? DateTimeOffset.FromUnixTimeSeconds(response.ExpiresAt)
                : DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn),
            UserId = response.User.Id,
            Email = response.User.Email ?? ""
        };
        await _storage.SaveAsync(SessionStorageKey, session);
        return session;
    }

    private static void ApplyHeaders(HttpRequestMessage request, SupabaseSettings settings, string? accessToken)
    {
        request.Headers.Add("apikey", settings.AnonKey);
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string? TryReadAuthError(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            foreach (var name in new[] { "error_description", "msg", "message", "error" })
            {
                if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private sealed class AuthResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
        [JsonPropertyName("expires_in")] public long ExpiresIn { get; set; }
        [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
        [JsonPropertyName("user")] public AuthUser? User { get; set; }
    }

    private sealed class AuthUser
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("email")] public string? Email { get; set; }
    }

    private sealed class SupabaseAuthException : Exception
    {
        public SupabaseAuthException(string message) : base(message) { }
    }
}

public class SupabaseRecordHeader
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("hash")] public string? Hash { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
}

public class SupabaseRecord : SupabaseRecordHeader
{
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }
}

public class SupabaseRecordWrite
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }
    [JsonPropertyName("hash")] public string? Hash { get; set; }
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
}
