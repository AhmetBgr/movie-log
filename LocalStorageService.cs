using Microsoft.JSInterop;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

public class LocalStorageService
{
    private readonly IJSRuntime _js;

    public LocalStorageService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<string> ExportAllAsync()
    {
        return await _js.InvokeAsync<string>("localStorageFunctions.getAllItems");
    }

    public async Task ImportAllAsync(string json)
    {
        await _js.InvokeVoidAsync("localStorageFunctions.setAllItems", json);
    }

    // ── Plain (uncompressed) helpers ─────────────────────────────────────────

    public async Task SaveAsync<T>(string key, T data)
    {
        var json = JsonSerializer.Serialize(data);
        await _js.InvokeVoidAsync("localStorageFunctions.setItem", key, json);
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var json = await _js.InvokeAsync<string>("localStorageFunctions.getItem", key);
        if (string.IsNullOrEmpty(json)) return default;
        try { return JsonSerializer.Deserialize<T>(json); } catch { return default; }
    }

    public async Task SaveListAsync<T>(string key, List<T> list) => await SaveAsync(key, list);
    public async Task<List<T>> GetListAsync<T>(string key) => await GetAsync<List<T>>(key) ?? new List<T>();

    // ── Compressed helpers (Deflate → Base64) ────────────────────────────────

    public async Task SaveCompressedAsync<T>(string key, T data)
    {
        var json = JsonSerializer.Serialize(data);
        var compressed = Compress(json);
        await _js.InvokeVoidAsync("localStorageFunctions.setItem", key, compressed);
    }

    public async Task<T?> GetCompressedAsync<T>(string key)
    {
        var compressed = await _js.InvokeAsync<string>("localStorageFunctions.getItem", key);
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
        => await _js.InvokeVoidAsync("localStorageFunctions.removeItem", key);

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
