using Microsoft.JSInterop;
using System.Text.Json;

public class LocalStorageService
{
    private readonly IJSRuntime _js;

    public LocalStorageService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task SaveListAsync<T>(string key, List<T> list)
    {
        var json = JsonSerializer.Serialize(list);
        await _js.InvokeVoidAsync("localStorageFunctions.setItem", key, json);
    }

    public async Task<List<T>> GetListAsync<T>(string key)
    {
        var json = await _js.InvokeAsync<string>("localStorageFunctions.getItem", key);
        if (string.IsNullOrEmpty(json)) return new List<T>();

        return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
    }
}