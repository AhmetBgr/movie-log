using System.Text.Json;
using MyPrivateWatchlist.Models;

namespace MyPrivateWatchlist.Services;

public enum SupabaseSyncMode
{
    /// <summary>Per-record three-way merge against the last synced state.</summary>
    Smart,
    /// <summary>Make the local library match Supabase exactly.</summary>
    ForcePull,
    /// <summary>Make Supabase match the local library exactly.</summary>
    ForcePush
}

/// <summary>
/// Syncs the full library (every item field + custom collections) with Supabase, one row per record.
///
/// For every record we remember the local and remote hash from the last sync (the "baseline").
/// On sync, a side whose hash moved away from its baseline has changed:
///   only local changed  → push,  only remote changed → pull.
///   both changed        → local wins if this device has synced the record before,
///                         remote wins on a device's first sync (so a fresh device joins the cloud copy).
/// Deletions are pushed as tombstones (deleted = true) so other devices can apply them.
/// </summary>
public class SupabaseSyncService
{
    private const string StateStorageKey = "supabase_sync_state";
    private const string ItemKind = "item";
    private const string CollectionKind = "collection";

    // A smart sync that would delete at least this many records, and this share of one side, is refused.
    private const int MassDeleteMinCount = 10;
    private const double MassDeleteMinShare = 0.2;

    private readonly SupabaseApi _api;
    private readonly WatchlistService _watchlistSvc;
    private readonly LocalStorageService _storage;
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private SyncState? _state;

    public DateTimeOffset? LastSyncAt => _state?.LastSyncAt;
    public string? LastSyncSummary => _state?.LastSyncSummary;
    public string? LastError { get; private set; }
    public string? SignedInEmail { get; private set; }
    public bool IsSignedIn => !string.IsNullOrWhiteSpace(SignedInEmail);
    public SyncPreview? Pending { get; private set; }
    public event Action? OnStatusChanged;

    public SupabaseSyncService(SupabaseApi api, WatchlistService watchlistSvc, LocalStorageService storage)
    {
        _api = api;
        _watchlistSvc = watchlistSvc;
        _storage = storage;
    }

    public Task<SupabaseSettings> GetSettingsAsync() => _api.GetSettingsAsync();

    public async Task InitializeAsync()
    {
        await LoadStateAsync();
        SignedInEmail = (await _api.GetStoredSessionAsync())?.Email;
        NotifyStatusChanged();
    }

    public async Task SaveSettingsAsync(SupabaseSettings settings)
    {
        var previous = await _api.GetSettingsAsync();
        await _api.SaveSettingsAsync(settings);

        var normalizedUrl = (settings.ProjectUrl ?? "").Trim().TrimEnd('/');
        if (!string.Equals(previous.ProjectUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
        {
            // A different project means a different database: the old session and baseline don't apply.
            await SignOutAsync();
        }
    }

    public async Task SignInAsync(string email, string password)
    {
        try
        {
            var session = await _api.SignInAsync(email, password);
            SignedInEmail = session.Email;
            LastError = null;

            var state = await LoadStateAsync();
            if (!string.Equals(state.UserId, session.UserId, StringComparison.Ordinal))
            {
                _state = new SyncState { UserId = session.UserId };
                await SaveStateAsync();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally
        {
            NotifyStatusChanged();
        }
    }

    public async Task SignOutAsync()
    {
        await _api.SignOutAsync();
        _state = new SyncState();
        await SaveStateAsync();
        SignedInEmail = null;
        Pending = null;
        NotifyStatusChanged();
    }

    /// <summary>Computes what a smart sync would do, without changing anything.</summary>
    public async Task RefreshStatusAsync()
    {
        if (!IsSignedIn) return;
        try
        {
            await EnsureWatchlistReadyAsync();
            var state = await LoadStateAsync();
            var local = BuildLocalSnapshot();
            var remote = await FetchRemoteHeadersAsync();
            Pending = Plan(SupabaseSyncMode.Smart, local, remote, state.Baseline).Preview;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        NotifyStatusChanged();
    }

    public async Task<SyncPreview> SyncAsync(SupabaseSyncMode mode)
    {
        if (!await _syncGate.WaitAsync(0))
            throw new InvalidOperationException("A Supabase sync is already running.");

        try
        {
            await EnsureWatchlistReadyAsync();
            var state = await LoadStateAsync();
            var local = BuildLocalSnapshot();
            var remote = await FetchRemoteHeadersAsync();
            var plan = Plan(mode, local, remote, state.Baseline);

            if (mode == SupabaseSyncMode.Smart)
                GuardAgainstMassDelete(plan, local, remote);

            await PushAsync(plan.Pushes, local);
            var pulled = await PullAsync(plan.Pulls, remote);

            if (plan.Pulls.Count > 0)
            {
                var (items, collections) = ApplyPulls(pulled);
                await _watchlistSvc.UpdateListAndCollectionsAsync(items, collections);
            }

            UpdateBaseline(state, plan, local, remote, pulled);
            state.LastSyncAt = DateTimeOffset.Now;
            state.LastSyncSummary = plan.Preview.Describe(mode);
            await SaveStateAsync();

            Pending = new SyncPreview();
            LastError = null;
            return plan.Preview;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally
        {
            _syncGate.Release();
            NotifyStatusChanged();
        }
    }

    // ── Planning ─────────────────────────────────────────────────────────

    private static SyncPlan Plan(
        SupabaseSyncMode mode,
        Dictionary<string, LocalRecord> local,
        Dictionary<string, SupabaseRecordHeader> remote,
        Dictionary<string, BaselineEntry> baseline)
    {
        var plan = new SyncPlan();
        var keys = new HashSet<string>(local.Keys, StringComparer.Ordinal);
        keys.UnionWith(remote.Keys);
        keys.UnionWith(baseline.Keys);

        foreach (var key in keys)
        {
            var localHash = local.TryGetValue(key, out var l) ? l.Hash : null;
            var remoteHash = remote.TryGetValue(key, out var r) && !r.Deleted ? r.Hash : null;
            baseline.TryGetValue(key, out var b);

            var localChanged = b != null ? localHash != b.LocalHash : localHash != null;
            var remoteChanged = b != null ? remoteHash != b.RemoteHash : remoteHash != null;

            // Already matching on both sides (or gone on both): nothing to move.
            if (!localChanged && !remoteChanged) continue;
            if (localHash == null && remoteHash == null) continue;
            if (localHash != null && localHash == remoteHash)
            {
                plan.Matched.Add(key);
                continue;
            }

            var action = mode switch
            {
                SupabaseSyncMode.ForcePull => SyncAction.Pull,
                SupabaseSyncMode.ForcePush => SyncAction.Push,
                _ when localChanged && !remoteChanged => SyncAction.Push,
                _ when !localChanged && remoteChanged => SyncAction.Pull,
                _ => b != null ? SyncAction.Push : SyncAction.Pull
            };

            var isConflict = mode == SupabaseSyncMode.Smart && localChanged && remoteChanged;
            if (isConflict) plan.Preview.Conflicts++;

            if (action == SyncAction.Push)
            {
                plan.Pushes.Add(key);
                if (localHash == null) plan.Preview.PushDeletes++;
                else plan.Preview.PushUpserts++;
            }
            else
            {
                plan.Pulls.Add(key);
                if (remoteHash == null) plan.Preview.PullDeletes++;
                else plan.Preview.PullUpserts++;
            }
        }

        return plan;
    }

    private static void GuardAgainstMassDelete(SyncPlan plan, Dictionary<string, LocalRecord> local, Dictionary<string, SupabaseRecordHeader> remote)
    {
        var remoteLive = remote.Values.Count(r => !r.Deleted);
        if (plan.Preview.PushDeletes >= MassDeleteMinCount && plan.Preview.PushDeletes >= remoteLive * MassDeleteMinShare)
            throw new InvalidOperationException(
                $"Smart Sync would delete {plan.Preview.PushDeletes} records from Supabase. " +
                "If your local library was cleared by mistake, use Pull to restore it. If the deletion is intended, use Push.");

        if (plan.Preview.PullDeletes >= MassDeleteMinCount && plan.Preview.PullDeletes >= local.Count * MassDeleteMinShare)
            throw new InvalidOperationException(
                $"Smart Sync would delete {plan.Preview.PullDeletes} records from this device. " +
                "If the Supabase data was removed by mistake, use Push to restore it. If the deletion is intended, use Pull.");
    }

    // ── Execution ────────────────────────────────────────────────────────

    private async Task PushAsync(List<string> keys, Dictionary<string, LocalRecord> local)
    {
        var writes = keys.Select(key =>
        {
            var (kind, id) = SplitKey(key);
            if (local.TryGetValue(key, out var record))
            {
                using var doc = JsonDocument.Parse(record.Json);
                return new SupabaseRecordWrite { Kind = kind, Key = id, Data = doc.RootElement.Clone(), Hash = record.Hash, Deleted = false };
            }
            return new SupabaseRecordWrite { Kind = kind, Key = id, Data = null, Hash = null, Deleted = true };
        }).ToList();

        await _api.UpsertRecordsAsync(writes);
    }

    private async Task<Dictionary<string, PulledRecord>> PullAsync(List<string> keys, Dictionary<string, SupabaseRecordHeader> remote)
    {
        var pulled = new Dictionary<string, PulledRecord>(StringComparer.Ordinal);

        // Deletions need no payload.
        foreach (var key in keys.Where(k => !remote.TryGetValue(k, out var r) || r.Deleted))
            pulled[key] = new PulledRecord();

        foreach (var group in keys.Where(k => !pulled.ContainsKey(k)).Select(SplitKey).GroupBy(k => k.Kind))
        {
            var records = await _api.FetchRecordsAsync(group.Key, group.Select(k => k.Id));
            foreach (var record in records)
            {
                var key = MakeKey(record.Kind, record.Key);
                if (record.Deleted || record.Data is not { ValueKind: JsonValueKind.Object } data)
                {
                    pulled[key] = new PulledRecord();
                    continue;
                }

                var json = data.GetRawText();
                if (record.Kind == ItemKind)
                {
                    var item = WatchlistSyncData.DeserializeItem(json);
                    pulled[key] = new PulledRecord { Item = item, LocalHash = WatchlistSyncData.HashText(WatchlistSyncData.SerializeItem(item)) };
                }
                else
                {
                    var collection = WatchlistSyncData.DeserializeCollection(json);
                    pulled[key] = new PulledRecord { Collection = collection, LocalHash = WatchlistSyncData.HashText(WatchlistSyncData.SerializeCollection(collection)) };
                }
            }
        }

        var missing = keys.Where(k => !pulled.ContainsKey(k)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"{missing.Count} records changed on Supabase while syncing. Please sync again.");

        return pulled;
    }

    private (List<WatchlistItem> Items, List<CustomCollection> Collections) ApplyPulls(Dictionary<string, PulledRecord> pulled)
    {
        var applied = new HashSet<string>(StringComparer.Ordinal);

        var items = new List<WatchlistItem>();
        foreach (var item in _watchlistSvc.Items)
        {
            var key = string.IsNullOrWhiteSpace(item.ImdbId) ? null : MakeKey(ItemKind, item.ImdbId);
            if (key == null || !pulled.TryGetValue(key, out var p))
            {
                items.Add(item);
                continue;
            }
            if (applied.Add(key) && p.Item != null) items.Add(p.Item);
        }
        items.AddRange(pulled.Where(kv => kv.Value.Item != null && applied.Add(kv.Key)).Select(kv => kv.Value.Item!));

        var collections = new List<CustomCollection>();
        foreach (var collection in _watchlistSvc.Collections)
        {
            var key = MakeKey(CollectionKind, collection.Id.ToString());
            if (!pulled.TryGetValue(key, out var p))
            {
                collections.Add(collection);
                continue;
            }
            if (applied.Add(key) && p.Collection != null) collections.Add(p.Collection);
        }
        collections.AddRange(pulled.Where(kv => kv.Value.Collection != null && applied.Add(kv.Key)).Select(kv => kv.Value.Collection!));

        return (items, collections);
    }

    private static void UpdateBaseline(
        SyncState state,
        SyncPlan plan,
        Dictionary<string, LocalRecord> local,
        Dictionary<string, SupabaseRecordHeader> remote,
        Dictionary<string, PulledRecord> pulled)
    {
        foreach (var key in plan.Pushes)
        {
            if (local.TryGetValue(key, out var l)) state.Baseline[key] = new BaselineEntry(l.Hash, l.Hash);
            else state.Baseline.Remove(key);
        }

        foreach (var key in plan.Pulls)
        {
            var p = pulled[key];
            if (p.LocalHash != null && remote.TryGetValue(key, out var r)) state.Baseline[key] = new BaselineEntry(p.LocalHash, r.Hash);
            else state.Baseline.Remove(key);
        }

        foreach (var key in plan.Matched)
        {
            var hash = local[key].Hash;
            state.Baseline[key] = new BaselineEntry(hash, hash);
        }

        // First sync where both sides already agree record-for-record.
        foreach (var (key, l) in local)
        {
            if (!state.Baseline.ContainsKey(key) && remote.TryGetValue(key, out var r) && !r.Deleted && r.Hash == l.Hash)
                state.Baseline[key] = new BaselineEntry(l.Hash, r.Hash);
        }
    }

    // ── Snapshots ────────────────────────────────────────────────────────

    private Dictionary<string, LocalRecord> BuildLocalSnapshot()
    {
        var result = new Dictionary<string, LocalRecord>(StringComparer.Ordinal);

        foreach (var item in _watchlistSvc.Items.ToList())
        {
            if (string.IsNullOrWhiteSpace(item.ImdbId)) continue;
            var key = MakeKey(ItemKind, item.ImdbId);
            if (result.ContainsKey(key)) continue;
            var json = WatchlistSyncData.SerializeItem(item);
            result[key] = new LocalRecord(json, WatchlistSyncData.HashText(json));
        }

        foreach (var collection in _watchlistSvc.Collections.ToList())
        {
            var key = MakeKey(CollectionKind, collection.Id.ToString());
            if (result.ContainsKey(key)) continue;
            var json = WatchlistSyncData.SerializeCollection(collection);
            result[key] = new LocalRecord(json, WatchlistSyncData.HashText(json));
        }

        return result;
    }

    private async Task<Dictionary<string, SupabaseRecordHeader>> FetchRemoteHeadersAsync()
    {
        var headers = await _api.FetchRecordHeadersAsync();
        return headers
            .Where(h => h.Kind is ItemKind or CollectionKind)
            .ToDictionary(h => MakeKey(h.Kind, h.Key), StringComparer.Ordinal);
    }

    // An empty list while IndexedDB is still loading would look like "everything was deleted".
    private async Task EnsureWatchlistReadyAsync()
    {
        await _watchlistSvc.InitializeAsync();
        if (_watchlistSvc.IsInitializing)
            throw new InvalidOperationException("The library is still loading. Please try again in a moment.");
    }

    private static string MakeKey(string kind, string id) => $"{kind}:{id}";

    private static (string Kind, string Id) SplitKey(string key)
    {
        var colon = key.IndexOf(':');
        return (key[..colon], key[(colon + 1)..]);
    }

    // ── State ────────────────────────────────────────────────────────────

    private async Task<SyncState> LoadStateAsync()
        => _state ??= await _storage.GetAsync<SyncState>(StateStorageKey) ?? new SyncState();

    private async Task SaveStateAsync()
        => await _storage.SaveAsync(StateStorageKey, _state ?? new SyncState());

    private void NotifyStatusChanged() => OnStatusChanged?.Invoke();

    private enum SyncAction { Push, Pull }

    private sealed record LocalRecord(string Json, string Hash);

    public sealed record BaselineEntry(string? LocalHash, string? RemoteHash);

    private sealed class PulledRecord
    {
        public WatchlistItem? Item { get; init; }
        public CustomCollection? Collection { get; init; }
        /// <summary>Hash of the record as serialized by this device (null for deletions).</summary>
        public string? LocalHash { get; init; }
    }

    private sealed class SyncPlan
    {
        public List<string> Pushes { get; } = new();
        public List<string> Pulls { get; } = new();
        public List<string> Matched { get; } = new();
        public SyncPreview Preview { get; } = new();
    }

    public sealed class SyncState
    {
        public string? UserId { get; set; }
        public Dictionary<string, BaselineEntry> Baseline { get; set; } = new(StringComparer.Ordinal);
        public DateTimeOffset? LastSyncAt { get; set; }
        public string? LastSyncSummary { get; set; }
    }
}

public sealed class SyncPreview
{
    public int PushUpserts { get; set; }
    public int PushDeletes { get; set; }
    public int PullUpserts { get; set; }
    public int PullDeletes { get; set; }
    public int Conflicts { get; set; }

    public bool HasChanges => PushUpserts + PushDeletes + PullUpserts + PullDeletes > 0;

    public string Describe(SupabaseSyncMode mode)
    {
        if (!HasChanges) return "Already up to date.";
        var parts = new List<string>();
        if (PushUpserts > 0) parts.Add($"uploaded {PushUpserts}");
        if (PushDeletes > 0) parts.Add($"deleted {PushDeletes} remotely");
        if (PullUpserts > 0) parts.Add($"downloaded {PullUpserts}");
        if (PullDeletes > 0) parts.Add($"deleted {PullDeletes} locally");
        var text = string.Join(", ", parts);
        text = char.ToUpperInvariant(text[0]) + text[1..];
        if (Conflicts > 0) text += $" ({Conflicts} conflicts resolved)";
        return mode == SupabaseSyncMode.Smart ? text + "." : $"{text} ({mode}).";
    }
}
