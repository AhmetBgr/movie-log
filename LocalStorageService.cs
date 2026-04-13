using Microsoft.JSInterop;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

public class LocalStorageService
{
    private readonly IJSRuntime _js;
    private bool _migrationChecked = false;
    private bool _migrationDone = false;

    public LocalStorageService(IJSRuntime js)
    {
        _js = js;
    }

    private async Task EnsureMigratedAsync()
    {
        if (_migrationDone) return;
        if (_migrationChecked) return; 
        _migrationChecked = true;

        try
        {
            var migrationFlag = await _js.InvokeAsync<string>("storageFunctions.getLegacyItem", "idb_migrated");
            if (migrationFlag == "true") 
            {
                _migrationDone = true;
                return;
            }

            Console.WriteLine("[Storage] Starting migration from localStorage to IndexedDB...");
            var keys = await _js.InvokeAsync<List<string>>("storageFunctions.getLegacyKeys");
            
            foreach (var key in keys)
            {
                var value = await _js.InvokeAsync<string>("storageFunctions.getLegacyItem", key);
                if (value != null)
                {
                    await _js.InvokeVoidAsync("storageFunctions.setItem", key, value);
                }
            }

            await _js.InvokeVoidAsync("storageFunctions.setItem", "idb_migrated", "true");
            _migrationDone = true;
            Console.WriteLine("[Storage] Migration complete.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Storage] Migration error: {ex.Message}");
            _migrationDone = true; 
        }
    }

    public async Task<string> ExportAllAsync()
    {
        if (!_migrationDone) await EnsureMigratedAsync();
        return await _js.InvokeAsync<string>("storageFunctions.getAllItems");
    }

    public async Task ImportAllAsync(string json)
    {
        if (!_migrationDone) await EnsureMigratedAsync();
        var items = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (items != null)
        {
            await _js.InvokeVoidAsync("storageFunctions.clearAll");
            foreach (var kvp in items)
            {
                await _js.InvokeVoidAsync("storageFunctions.setItem", kvp.Key, kvp.Value);
            }
        }
    }

    // ── IndexedDB Helpers ───────────────────────────────────────────────────

    public async Task SaveAsync<T>(string key, T data)
    {
        if (!_migrationDone) await EnsureMigratedAsync();
        var json = JsonSerializer.Serialize(data);
        await _js.InvokeVoidAsync("storageFunctions.setItem", key, json);
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        if (!_migrationDone) await EnsureMigratedAsync();
        var json = await _js.InvokeAsync<string>("storageFunctions.getItem", key);
        if (string.IsNullOrEmpty(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); } catch { return default; }
    }

    public async Task SaveListAsync<T>(string key, List<T> list) => await SaveAsync(key, list);
    public async Task<List<T>> GetListAsync<T>(string key) => await GetAsync<List<T>>(key) ?? new List<T>();

    // ── Compressed helpers (Deflate → Base64) ────────────────────────────────

    public async Task SaveCompressedAsync<T>(string key, T data)
    {
        if (!_migrationDone) await EnsureMigratedAsync();
        var json = JsonSerializer.Serialize(data);
        var compressed = Compress(json);
        await _js.InvokeVoidAsync("storageFunctions.setItem", key, compressed);
    }

    public async Task<T?> GetCompressedAsync<T>(string key)
    {
        if (!_migrationDone) await EnsureMigratedAsync();
        var compressed = await _js.InvokeAsync<string>("storageFunctions.getItem", key);
        if (string.IsNullOrEmpty(compressed)) return default;
        try
        {
            var json = Decompress(compressed);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch { return default; }
    }

    public async Task<List<T>> GetCompressedListAsync<T>(string key)
        => await GetCompressedAsync<List<T>>(key) ?? new List<T>();

    public async Task SaveCompressedListAsync<T>(string key, List<T> list)
        => await SaveCompressedAsync(key, list);

    public async Task RemoveAsync(string key)
    {
        if (!_migrationDone) await EnsureMigratedAsync();
        await _js.InvokeVoidAsync("storageFunctions.removeItem", key);
    }

    // ── Deflate compress / decompress ────────────────────────────────────────

    private static string Compress(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
            deflate.Write(bytes, 0, bytes.Length);
        return Convert.ToBase64String(output.ToArray());
    }

    private static string Decompress(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        using var input = new MemoryStream(bytes);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
