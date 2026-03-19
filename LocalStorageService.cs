using Microsoft.JSInterop;
using System.Text.Json;

public class LocalStorageService
{
    private readonly IJSRuntime _js;

    public LocalStorageService(IJSRuntime js)
    {
        _js = js;
    }

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
}