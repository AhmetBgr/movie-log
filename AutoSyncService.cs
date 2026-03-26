using MyPrivateWatchlist.Models;

namespace MyPrivateWatchlist.Services;

public class AutoSyncService : IDisposable
{
    private const string SyncStateStorageKey = "gist_sync_state";

    private readonly WatchlistService _watchlistSvc;
    private readonly GistSyncService _gistSyncSvc;
    private readonly LocalStorageService _storage;
    private readonly object _gate = new();

    private bool _started;
    private bool _applyingRemote;
    private string? _lastSyncedHash;
    private DateTimeOffset? _lastLocalChangeAt;

    public DateTimeOffset? LastSyncAt { get; private set; }
    public string LastSyncDirection { get; private set; } = "Never";
    public string? LastError { get; private set; }
    public bool HasUnsyncedLocalChanges { get; private set; }
    public bool HasRemoteChanges { get; private set; }
    public bool HasConflict { get; private set; }
    public string SyncStateLabel { get; private set; } = "Sync status unknown";
    public string? LastRemoteSummary { get; private set; }
    public event Action? OnStatusChanged;

    public AutoSyncService(WatchlistService watchlistSvc, GistSyncService gistSyncSvc, LocalStorageService storage)
    {
        _watchlistSvc = watchlistSvc;
        _gistSyncSvc = gistSyncSvc;
        _storage = storage;
    }

    public async Task EnsureStartedAsync()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }

        _watchlistSvc.OnStateChanged += HandleWatchlistStateChanged;
        await WaitForWatchlistReadyAsync();

        var savedState = await _storage.GetAsync<SyncStateRecord>(SyncStateStorageKey);
        _lastSyncedHash = savedState?.LastSyncedHash;
        LastSyncAt = savedState?.LastSyncAt;
        LastSyncDirection = savedState?.LastSyncDirection ?? "Never";
        UpdateDerivedState(null, null, remoteSummary: null);
        NotifyStatusChanged();
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            var settings = await _gistSyncSvc.GetSettingsAsync();
            if (!HasCredentials(settings))
            {
                LastRemoteSummary = null;
                LastError = null;
                UpdateDerivedState(null, null, "Sync settings are incomplete.");
                NotifyStatusChanged();
                return;
            }

            var remote = await _gistSyncSvc.LoadSnapshotAsync();
            LastError = remote.WarningMessage;
            UpdateDerivedState(remote.Hash, remote.UpdatedAt, remote.DescribeSource());
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            UpdateDerivedState(null, null, "Remote status unavailable.");
        }

        NotifyStatusChanged();
    }

    public async Task SyncNowAsync()
    {
        try
        {
            var settings = await _gistSyncSvc.GetSettingsAsync();
            if (!HasCredentials(settings))
                throw new InvalidOperationException("Gist settings are missing. Please provide both Gist ID and Personal Access Token.");

            var localItems = _watchlistSvc.Items.ToList();
            var localHash = WatchlistSyncData.ComputeHash(localItems);
            var remote = await _gistSyncSvc.LoadSnapshotAsync();
            var remoteHash = remote.Hash;

            if (string.IsNullOrWhiteSpace(_lastSyncedHash))
            {
                if (localHash == remoteHash)
                {
                    await MarkSyncedAsync(localHash, "Sync (already up to date)");
                    LastError = remote.WarningMessage;
                    UpdateDerivedState(remoteHash, remote.UpdatedAt, remote.DescribeSource());
                    return;
                }

                if (!localItems.Any() && remote.Items.Any())
                {
                    await ApplyRemoteAsync(remote, "Pull");
                    return;
                }

                if (localItems.Any() && !remote.Items.Any())
                {
                    await PushLocalAsync(localItems, localHash, "Push");
                    return;
                }
            }

            var localChanged = !string.Equals(localHash, _lastSyncedHash, StringComparison.Ordinal);
            var remoteChanged = !string.Equals(remoteHash, _lastSyncedHash, StringComparison.Ordinal);

            if (!localChanged && !remoteChanged)
            {
                await MarkSyncedAsync(localHash, "Sync (already up to date)");
                LastError = remote.WarningMessage;
                UpdateDerivedState(remoteHash, remote.UpdatedAt, remote.DescribeSource());
                return;
            }

            if (localChanged && !remoteChanged)
            {
                await PushLocalAsync(localItems, localHash, "Push");
                return;
            }

            if (!localChanged && remoteChanged)
            {
                await ApplyRemoteAsync(remote, "Pull");
                return;
            }

            if (localHash == remoteHash)
            {
                await MarkSyncedAsync(localHash, "Sync (matched)");
                LastError = remote.WarningMessage;
                UpdateDerivedState(remoteHash, remote.UpdatedAt, remote.DescribeSource());
                return;
            }

            var localChangeAt = _lastLocalChangeAt ?? DateTimeOffset.MinValue;
            var remoteChangedAt = remote.UpdatedAt ?? DateTimeOffset.MinValue;

            if (remoteChangedAt > localChangeAt)
            {
                await ApplyRemoteAsync(remote, "Pull");
            }
            else
            {
                await PushLocalAsync(localItems, localHash, "Push");
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            NotifyStatusChanged();
            throw;
        }
    }

    public void MarkLocalStateApplied()
    {
        var localHash = WatchlistSyncData.ComputeHash(_watchlistSvc.Items);
        UpdateDerivedState(localHash, null, LastRemoteSummary);
        NotifyStatusChanged();
    }

    private void HandleWatchlistStateChanged()
    {
        if (_watchlistSvc.IsInitializing) return;
        if (_applyingRemote) return;

        _lastLocalChangeAt = DateTimeOffset.UtcNow;
        UpdateDerivedState(null, null, LastRemoteSummary);
        NotifyStatusChanged();
    }

    private async Task ApplyRemoteAsync(GistSnapshot remote, string direction)
    {
        _applyingRemote = true;
        try
        {
            await _watchlistSvc.UpdateListAsync(remote.Items);
            await MarkSyncedAsync(remote.Hash, direction);
            LastError = remote.WarningMessage;
            UpdateDerivedState(remote.Hash, remote.UpdatedAt, remote.DescribeSource());
            NotifyStatusChanged();
        }
        finally
        {
            _applyingRemote = false;
        }
    }

    private async Task PushLocalAsync(List<WatchlistItem> items, string localHash, string direction)
    {
        var saveResult = await _gistSyncSvc.SaveToGistAsync(items);
        await MarkSyncedAsync(localHash, direction);
        LastError = saveResult.WarningMessage;
        UpdateDerivedState(saveResult.Hash, saveResult.UpdatedAt, saveResult.DescribeSource());
        NotifyStatusChanged();
    }

    private async Task MarkSyncedAsync(string syncedHash, string direction)
    {
        _lastSyncedHash = syncedHash;
        LastSyncAt = DateTimeOffset.Now;
        LastSyncDirection = direction;
        _lastLocalChangeAt = null;
        await PersistStateAsync();
    }

    private async Task PersistStateAsync()
    {
        await _storage.SaveAsync(SyncStateStorageKey, new SyncStateRecord
        {
            LastSyncedHash = _lastSyncedHash,
            LastSyncAt = LastSyncAt,
            LastSyncDirection = LastSyncDirection
        });
    }

    private void UpdateDerivedState(string? remoteHash, DateTimeOffset? remoteUpdatedAt, string? remoteSummary)
    {
        var localHash = WatchlistSyncData.ComputeHash(_watchlistSvc.Items);
        var localChanged = !string.IsNullOrWhiteSpace(_lastSyncedHash) &&
                           !string.Equals(localHash, _lastSyncedHash, StringComparison.Ordinal);
        var remoteChanged = !string.IsNullOrWhiteSpace(remoteHash) &&
                            !string.Equals(remoteHash, _lastSyncedHash, StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(_lastSyncedHash) && !string.IsNullOrWhiteSpace(remoteHash))
        {
            localChanged = _watchlistSvc.Items.Any() && !string.Equals(localHash, remoteHash, StringComparison.Ordinal);
            remoteChanged = !_watchlistSvc.Items.Any() && !string.Equals(localHash, remoteHash, StringComparison.Ordinal);
        }

        HasUnsyncedLocalChanges = localChanged;
        HasRemoteChanges = remoteChanged;
        HasConflict = localChanged && remoteChanged && !string.Equals(localHash, remoteHash, StringComparison.Ordinal);
        LastRemoteSummary = remoteSummary;

        SyncStateLabel = HasConflict
            ? "Both local and gist data changed since the last sync."
            : HasUnsyncedLocalChanges
                ? "Local changes are waiting to be synced."
                : HasRemoteChanges
                    ? "The gist has newer data available."
                    : string.IsNullOrWhiteSpace(_lastSyncedHash)
                        ? "No sync baseline yet."
                        : "Local and gist data are in sync.";

        if (remoteUpdatedAt.HasValue && !string.IsNullOrWhiteSpace(remoteSummary))
        {
            LastRemoteSummary = $"{remoteSummary} checked {remoteUpdatedAt.Value.ToLocalTime():MMM dd, yyyy HH:mm:ss}";
        }
    }

    private void NotifyStatusChanged() => OnStatusChanged?.Invoke();

    private static bool HasCredentials(GistSettings settings)
        => !string.IsNullOrWhiteSpace(settings.GistId) && !string.IsNullOrWhiteSpace(settings.PersonalAccessToken);

    private async Task WaitForWatchlistReadyAsync()
    {
        var attempts = 0;
        while (_watchlistSvc.IsInitializing && attempts < 120)
        {
            await Task.Delay(250);
            attempts++;
        }
    }

    public void Dispose()
    {
        _watchlistSvc.OnStateChanged -= HandleWatchlistStateChanged;
    }

    private sealed class SyncStateRecord
    {
        public string? LastSyncedHash { get; set; }
        public DateTimeOffset? LastSyncAt { get; set; }
        public string? LastSyncDirection { get; set; }
    }
}
