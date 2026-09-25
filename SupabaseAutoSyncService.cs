using Microsoft.JSInterop;

namespace MyPrivateWatchlist.Services;

/// <summary>
/// Keeps the library in sync with Supabase without user action:
///   • local edits are pushed a moment after they are saved,
///   • other devices' writes are picked up by a cheap poll (newest updated_at) while the tab is visible,
///   • the app syncs on startup, when the tab regains focus, when the device comes back online,
///     and flushes pending edits when the tab is hidden.
/// Every run is a Smart Sync, so merging and the mass-delete guard stay in <see cref="SupabaseSyncService"/>.
/// </summary>
public class SupabaseAutoSyncService : IDisposable
{
    private static readonly TimeSpan LocalChangeDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly SupabaseSyncService _sync;
    private readonly SupabaseApi _api;
    private readonly WatchlistService _watchlistSvc;
    private readonly IJSRuntime _js;

    private bool _started;
    private bool _visible = true;
    private bool _running;
    private bool _rerunRequested;
    private CancellationTokenSource? _debounceCts;
    private PeriodicTimer? _pollTimer;
    private DotNetObjectReference<SupabaseAutoSyncService>? _jsRef;

    public bool IsSyncing => _running;

    public SupabaseAutoSyncService(SupabaseSyncService sync, SupabaseApi api, WatchlistService watchlistSvc, IJSRuntime js)
    {
        _sync = sync;
        _api = api;
        _watchlistSvc = watchlistSvc;
        _js = js;
    }

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;

        await _sync.InitializeAsync();
        _watchlistSvc.OnLibraryPersisted += HandleLibraryPersisted;

        _jsRef = DotNetObjectReference.Create(this);
        try { _visible = await _js.InvokeAsync<bool>("syncHelpers.register", _jsRef); } catch { }

        _pollTimer = new PeriodicTimer(PollInterval);
        _ = PollLoopAsync(_pollTimer);
        _ = SyncNowAsync();
    }

    /// <summary>Runs a Smart Sync now (or right after the one in progress). Errors land in <see cref="SupabaseSyncService.LastError"/>.</summary>
    public async Task SyncNowAsync()
    {
        CancelDebounce();
        if (!await IsEnabledAsync()) return;

        if (_running)
        {
            _rerunRequested = true;
            return;
        }

        _running = true;
        try
        {
            do
            {
                _rerunRequested = false;
                await WaitForWatchlistReadyAsync();
                try
                {
                    await _sync.SyncAsync(SupabaseSyncMode.Smart);
                }
                catch
                {
                    // Already recorded as LastError; the next change, poll or focus retries.
                }
            } while (_rerunRequested);
        }
        finally
        {
            _running = false;
        }
    }

    [JSInvokable]
    public Task OnVisibilityChanged(bool visible)
    {
        _visible = visible;
        if (visible) return CheckRemoteAsync();
        // Leaving the tab: push pending edits before the browser may suspend or close it.
        return _debounceCts != null ? SyncNowAsync() : Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnOnline() => SyncNowAsync();

    private void HandleLibraryPersisted()
    {
        CancelDebounce();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebouncedSyncAsync(cts.Token);
    }

    private async Task DebouncedSyncAsync(CancellationToken token)
    {
        try { await Task.Delay(LocalChangeDelay, token); }
        catch (OperationCanceledException) { return; }
        await SyncNowAsync();
    }

    private async Task PollLoopAsync(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                if (_visible) await CheckRemoteAsync();
            }
        }
        catch (ObjectDisposedException) { }
    }

    // Only runs a full sync when another device wrote something since our last sync.
    private async Task CheckRemoteAsync()
    {
        if (_running || !await IsEnabledAsync()) return;
        try
        {
            var latest = await _api.FetchLatestUpdateAsync();
            var watermark = _sync.RemoteWatermark;
            if (_sync.LastSyncAt == null || (latest.HasValue && (watermark == null || latest > watermark)))
                await SyncNowAsync();
        }
        catch
        {
            // Offline or session trouble: the full sync reports it properly, so just try one.
            await SyncNowAsync();
        }
    }

    private async Task<bool> IsEnabledAsync()
        => _sync.IsSignedIn && (await _sync.GetSettingsAsync()).AutoSync;

    private async Task WaitForWatchlistReadyAsync()
    {
        _ = _watchlistSvc.InitializeAsync();
        for (var i = 0; _watchlistSvc.IsInitializing && i < 240; i++)
            await Task.Delay(250);
    }

    private void CancelDebounce()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }

    public void Dispose()
    {
        _watchlistSvc.OnLibraryPersisted -= HandleLibraryPersisted;
        CancelDebounce();
        _pollTimer?.Dispose();
        _jsRef?.Dispose();
    }
}
