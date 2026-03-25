using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MyPrivateWatchlist.Models;

namespace MyPrivateWatchlist.Services;

public class AutoSyncService : IDisposable
{
    private readonly WatchlistService _watchlistSvc;
    private readonly GistSyncService _gistSyncSvc;

    private readonly object _gate = new();
    private CancellationTokenSource? _pullLoopCts;
    private CancellationTokenSource? _pushDebounceCts;
    private bool _started;
    private bool _applyingRemote;
    private bool _isPushing;
    private string? _lastSyncedHash;

    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web);

    public AutoSyncService(WatchlistService watchlistSvc, GistSyncService gistSyncSvc)
    {
        _watchlistSvc = watchlistSvc;
        _gistSyncSvc = gistSyncSvc;
    }

    public async Task EnsureStartedAsync()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }

        _watchlistSvc.OnStateChanged += HandleWatchlistStateChanged;
        await RestartPullLoopAsync();
    }

    public async Task RestartPullLoopAsync()
    {
        CancellationTokenSource? oldCts;
        lock (_gate)
        {
            oldCts = _pullLoopCts;
            _pullLoopCts = new CancellationTokenSource();
        }

        if (oldCts != null)
        {
            try { oldCts.Cancel(); } catch { }
            oldCts.Dispose();
        }

        _ = RunPullLoopAsync(_pullLoopCts.Token);
    }

    private void HandleWatchlistStateChanged()
    {
        if (_watchlistSvc.IsInitializing) return;
        if (_applyingRemote) return;
        _ = DebounceAutoPushAsync();
    }

    private async Task DebounceAutoPushAsync()
    {
        var settings = await _gistSyncSvc.GetSettingsAsync();
        if (!IsPushEnabled(settings)) return;
        if (!HasCredentials(settings)) return;

        CancellationToken token;
        lock (_gate)
        {
            _pushDebounceCts?.Cancel();
            _pushDebounceCts?.Dispose();
            _pushDebounceCts = new CancellationTokenSource();
            token = _pushDebounceCts.Token;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), token);
            await TryAutoPushAsync(settings, token);
        }
        catch (OperationCanceledException)
        {
            // Expected while user is actively changing data.
        }
    }

    private async Task TryAutoPushAsync(GistSettings settings, CancellationToken token)
    {
        if (_isPushing) return;
        _isPushing = true;
        try
        {
            var items = _watchlistSvc.Items.ToList();
            var hash = ComputeHash(items);
            if (hash == _lastSyncedHash) return;
            token.ThrowIfCancellationRequested();
            await _gistSyncSvc.SaveToGistAsync(items);
            _lastSyncedHash = hash;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutoSync] Auto-push failed: {ex.Message}");
        }
        finally
        {
            _isPushing = false;
        }
    }

    private async Task RunPullLoopAsync(CancellationToken token)
    {
        // Small startup delay so app initialization can settle.
        try { await Task.Delay(TimeSpan.FromSeconds(10), token); } catch (OperationCanceledException) { return; }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var settings = await _gistSyncSvc.GetSettingsAsync();
                if (IsPullEnabled(settings) && HasCredentials(settings) && !_watchlistSvc.IsInitializing)
                {
                    await TryAutoPullAsync();
                }

                var intervalMinutes = Math.Max(1, settings.AutoPullIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoSync] Pull loop error: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(20), token); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task TryAutoPullAsync()
    {
        try
        {
            var remoteItems = await _gistSyncSvc.LoadFromGistAsync();
            var remoteHash = ComputeHash(remoteItems);
            var localHash = ComputeHash(_watchlistSvc.Items);

            if (remoteHash == localHash)
            {
                _lastSyncedHash = localHash;
                return;
            }

            _applyingRemote = true;
            await _watchlistSvc.UpdateListAsync(remoteItems);
            _lastSyncedHash = remoteHash;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutoSync] Auto-pull failed: {ex.Message}");
        }
        finally
        {
            _applyingRemote = false;
        }
    }

    private static bool HasCredentials(GistSettings settings)
        => !string.IsNullOrWhiteSpace(settings.GistId) && !string.IsNullOrWhiteSpace(settings.PersonalAccessToken);

    private static bool IsPushEnabled(GistSettings settings)
        => settings.AutoSyncMode == GistAutoSyncMode.AutoPush || settings.AutoSyncMode == GistAutoSyncMode.TwoWay;

    private static bool IsPullEnabled(GistSettings settings)
        => settings.AutoSyncMode == GistAutoSyncMode.AutoPull || settings.AutoSyncMode == GistAutoSyncMode.TwoWay;

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

        var json = JsonSerializer.Serialize(normalized, HashJsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    public void Dispose()
    {
        _watchlistSvc.OnStateChanged -= HandleWatchlistStateChanged;
        _pullLoopCts?.Cancel();
        _pullLoopCts?.Dispose();
        _pushDebounceCts?.Cancel();
        _pushDebounceCts?.Dispose();
    }
}
