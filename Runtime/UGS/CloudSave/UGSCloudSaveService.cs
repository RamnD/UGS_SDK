using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.CloudSave;
using UnityEngine;

/// <summary>
/// <see cref="ICloudSaveService{TKey}"/> implementation via Unity Gaming Services Cloud Save SDK 3.x.
/// <para>
/// <b>Storage:</b> local cache — PlayerPrefs (JSON), cloud — UGS Cloud Save (Player Data).
/// Cloud write clock is <c>__ts</c>; local parent version is <see cref="BaseTimestamp"/>.
/// </para>
/// <para>
/// <b>Serialization:</b> all values are JSON-serialized via Newtonsoft.Json.
/// Works with primitives (int, long, bool, string) and serializable classes/structs.
/// </para>
/// </summary>
public sealed class UGSCloudSaveService<TKey> : ICloudSaveService<TKey>
    where TKey : struct, Enum
{
    private const string TimestampCloudKey = "__ts";

    private readonly ISaveKeyMapper<TKey> _mapper;
    private readonly string _localPrefsKey;
    private readonly string _localTimestampPrefsKey;
    private readonly string _baseTimestampPrefsKey;
    private readonly object _syncGate = new object();

    private Dictionary<string, string> _local        = new();
    private Dictionary<string, string> _cloudSnapshot = new(); // pending conflict snapshot
    private DateTime?                  _cloudSnapshotTimestamp;
    private bool                       _localLoaded;
    private Task<SaveConflict?>        _loadTask;
    private Task<SaveConflict?>        _pushTask;

    /// <inheritdoc/>
    public DateTime? LocalTimestamp { get; private set; }

    /// <inheritdoc/>
    public DateTime? BaseTimestamp { get; private set; }

    /// <param name="mapper">Key mapper: enum → cloud string.</param>
    public UGSCloudSaveService(ISaveKeyMapper<TKey> mapper)
    {
        _mapper                 = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _localPrefsKey          = $"cloud_save_{typeof(TKey).Name}_local";
        _localTimestampPrefsKey = $"cloud_save_{typeof(TKey).Name}_ts";
        _baseTimestampPrefsKey  = $"cloud_save_{typeof(TKey).Name}_base_ts";
        // PlayerPrefs are main-thread only — do NOT load here.
        // Loading is lazy on first data access (EnsureLocalLoaded).
    }

    // ── Local access ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public TValue Get<TValue>(TKey key, TValue defaultValue = default)
    {
        return TryGet(key, out TValue value) ? value : defaultValue;
    }

    /// <inheritdoc/>
    public bool TryGet<TValue>(TKey key, out TValue value)
    {
        EnsureLocalLoaded();
        value = default;
        if (!_local.TryGetValue(_mapper.ToCloudKey(key), out var json))
            return false;

        try
        {
            value = JsonConvert.DeserializeObject<TValue>(json);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError(
                $"[CloudSave] Corrupt local value for key '{key}' — leaving raw JSON intact. {ex.Message}");
            throw new InvalidOperationException(
                $"Cloud save key '{key}' contains corrupt data and cannot be read safely.",
                ex);
        }
    }

    /// <inheritdoc/>
    public void Set<TValue>(TKey key, TValue value)
    {
        EnsureLocalLoaded();
        string cloudKey = _mapper.ToCloudKey(key);
        if (string.Equals(cloudKey, TimestampCloudKey, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Save key maps to reserved cloud key '{TimestampCloudKey}' and cannot be used for player data.",
                nameof(key));
        }

        _local[cloudKey] = JsonConvert.SerializeObject(value);
        LocalTimestamp = DateTime.UtcNow;
        PersistLocalToPrefs();
    }

    /// <inheritdoc/>
    public void ClearLocalCache()
    {
        EnsureLocalLoaded();
        _local.Clear();
        ClearCloudSnapshot();
        LocalTimestamp = null;
        BaseTimestamp = null;
        PlayerPrefs.DeleteKey(_localPrefsKey);
        PlayerPrefs.DeleteKey(_localTimestampPrefsKey);
        PlayerPrefs.DeleteKey(_baseTimestampPrefsKey);
        PlayerPrefs.Save();
        _localLoaded = true;
    }

    // ── Cloud sync ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SaveConflict?> LoadAsync(CancellationToken cancellationToken = default)
    {
        Task<SaveConflict?> load;
        lock (_syncGate)
        {
            if (_loadTask != null && !_loadTask.IsCompleted)
                load = _loadTask;
            else
            {
                load = LoadCoreAsync(cancellationToken);
                _loadTask = load;
            }
        }

        try
        {
            SaveConflict? result = await load;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            lock (_syncGate)
            {
                if (ReferenceEquals(_loadTask, load) && load.IsCompleted)
                    _loadTask = null;
            }
        }
    }

    async Task<SaveConflict?> LoadCoreAsync(CancellationToken cancellationToken)
    {
        EnsureLocalLoaded();
        if (!NetworkStatus.IsOnline) return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await CloudSaveService.Instance.Data.Player.LoadAllAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (items.Count == 0)
                return null; // cloud empty — keep local data

            ParseCloudItemsIntoSnapshot(items);

            Debug.Log($"[CloudSave] Loaded from cloud: {_cloudSnapshot.Count} keys, ts={_cloudSnapshotTimestamp:O}");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            foreach (var kvp in _cloudSnapshot)
                Debug.Log($"  [CloudSave] key={kvp.Key} bytes={kvp.Value?.Length ?? 0}");
#else
            Debug.Log($"[CloudSave] Cloud key names: {string.Join(", ", _cloudSnapshot.Keys)}");
#endif

            // Cloud has only __ts and no data — keep local data
            if (_cloudSnapshot.Count == 0)
            {
                Debug.Log("[CloudSave] Cloud has only timestamp payload — keeping local save.");
                ClearCloudSnapshot();
                return null;
            }

            // No local data → apply cloud without conflict
            if (!LocalTimestamp.HasValue)
            {
                ApplyCloud();
                return null;
            }

            var cloudTs = _cloudSnapshotTimestamp ?? DateTime.MinValue;

            // Local has no unsynced edits — take cloud if it moved ahead
            if (!IsDirty)
            {
                if (TimestampsMatch(cloudTs, BaseTimestamp) || TimestampsMatch(cloudTs, LocalTimestamp))
                {
                    BaseTimestamp = cloudTs;
                    PersistTimestampsToPrefs();
                    ClearCloudSnapshot();
                    return null;
                }

                ApplyCloud();
                return null;
            }

            // Local dirty, cloud still at our parent → keep local edits
            if (TimestampsMatch(cloudTs, BaseTimestamp))
            {
                ClearCloudSnapshot();
                return null;
            }

            // Local dirty and cloud moved since BaseTimestamp → conflict
            return new SaveConflict(LocalTimestamp.Value, cloudTs, SaveConflictSource.Load);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[CloudSave] Load failed: {e.Message}");
            throw new CloudSaveOperationException("Failed to load cloud save.", e);
        }
    }

    /// <inheritdoc/>
    public async Task<SaveConflict?> PushToCloudAsync(CancellationToken cancellationToken = default)
    {
        Task<SaveConflict?> push;
        lock (_syncGate)
        {
            if (_pushTask != null && !_pushTask.IsCompleted)
                push = _pushTask;
            else
            {
                push = PushCoreAsync(cancellationToken);
                _pushTask = push;
            }
        }

        try
        {
            SaveConflict? result = await push;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            lock (_syncGate)
            {
                if (ReferenceEquals(_pushTask, push) && push.IsCompleted)
                    _pushTask = null;
            }
        }
    }

    async Task<SaveConflict?> PushCoreAsync(CancellationToken cancellationToken)
    {
        EnsureLocalLoaded();
        if (!NetworkStatus.IsOnline) return null;
        if (!IsDirty) return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cloudTs = await LoadCloudTimestampAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Empty cloud or still at our parent version → safe to upload
            if (!cloudTs.HasValue || TimestampsMatch(cloudTs, BaseTimestamp))
            {
                await UploadLocalAsync(cancellationToken);
                return null;
            }

            // Cloud moved — load full snapshot for ApplyCloud / KeepLocal
            var items = await CloudSaveService.Instance.Data.Player.LoadAllAsync();
            cancellationToken.ThrowIfCancellationRequested();
            ParseCloudItemsIntoSnapshot(items);

            if (_cloudSnapshot.Count == 0)
            {
                // Only __ts / unexpected payload — treat as empty enough to overwrite
                await UploadLocalAsync(cancellationToken);
                return null;
            }

            var conflictCloudTs = _cloudSnapshotTimestamp ?? cloudTs.Value;
            Debug.LogWarning(
                $"[CloudSave] Push conflict — local dirty, cloud moved. " +
                $"base={BaseTimestamp:O}, cloud={conflictCloudTs:O}, local={LocalTimestamp:O}");

            return new SaveConflict(
                LocalTimestamp ?? DateTime.UtcNow,
                conflictCloudTs,
                SaveConflictSource.Push);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[CloudSave] Push failed: {e.Message}");
            throw new CloudSaveOperationException("Failed to push save to cloud.", e);
        }
    }

    /// <inheritdoc/>
    public void ApplyCloud()
    {
        _local         = new Dictionary<string, string>(_cloudSnapshot);
        LocalTimestamp = _cloudSnapshotTimestamp;
        BaseTimestamp  = _cloudSnapshotTimestamp;
        ClearCloudSnapshot();
        PersistLocalToPrefs();
        Debug.Log("[CloudSave] Applied cloud snapshot.");
    }

    /// <inheritdoc/>
    public void KeepLocal()
    {
        // Acknowledge the conflicting cloud version so the next push can overwrite it.
        if (_cloudSnapshotTimestamp.HasValue)
            BaseTimestamp = _cloudSnapshotTimestamp;

        ClearCloudSnapshot();
        PersistTimestampsToPrefs();
        Debug.Log("[CloudSave] Kept local save (base acknowledged for overwrite).");
    }

    // ── Internals ─────────────────────────────────────────────────────────

    bool IsDirty =>
        LocalTimestamp.HasValue && !TimestampsMatch(LocalTimestamp, BaseTimestamp);

    /// <summary>Exact version equality for dirty / optimistic concurrency (no ±1s tolerance).</summary>
    static bool TimestampsMatch(DateTime? a, DateTime? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        return a.Value.Ticks == b.Value.Ticks;
    }

    void ParseCloudItemsIntoSnapshot(IReadOnlyDictionary<string, Unity.Services.CloudSave.Models.Item> items)
    {
        _cloudSnapshot.Clear();
        _cloudSnapshotTimestamp = null;

        foreach (var kvp in items)
        {
            if (kvp.Key == TimestampCloudKey)
            {
                var tsStr = kvp.Value.Value.GetAs<string>();
                if (DateTime.TryParse(tsStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
                    _cloudSnapshotTimestamp = ts;
            }
            else
            {
                _cloudSnapshot[kvp.Key] = kvp.Value.Value.GetAs<string>();
            }
        }
    }

    async Task<DateTime?> LoadCloudTimestampAsync(CancellationToken cancellationToken)
    {
        var keys = new HashSet<string> { TimestampCloudKey };
        var items = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);
        cancellationToken.ThrowIfCancellationRequested();

        if (items == null || items.Count == 0)
            return null;

        if (!items.TryGetValue(TimestampCloudKey, out var item))
            return null;

        var tsStr = item.Value.GetAs<string>();
        if (DateTime.TryParse(tsStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts))
            return ts;

        return null;
    }

    async Task UploadLocalAsync(CancellationToken cancellationToken)
    {
        var ts = DateTime.UtcNow;
        var cloudData = new Dictionary<string, object>();

        foreach (var kvp in _local)
            cloudData[kvp.Key] = kvp.Value;

        cloudData[TimestampCloudKey] = ts.ToString("O");

        Debug.Log($"[CloudSave] Pushing to cloud: {_local.Count} keys + __ts");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        foreach (var kvp in _local)
            Debug.Log($"  [CloudSave] key={kvp.Key} bytes={kvp.Value?.Length ?? 0}");
#else
        Debug.Log($"[CloudSave] Push key names: {string.Join(", ", _local.Keys)}");
#endif

        await CloudSaveService.Instance.Data.Player.SaveAsync(cloudData);
        cancellationToken.ThrowIfCancellationRequested();

        LocalTimestamp = ts;
        BaseTimestamp  = ts;
        ClearCloudSnapshot();
        PersistLocalToPrefs();
        Debug.Log($"[CloudSave] Saved to cloud. Timestamp: {ts:O}");
    }

    void ClearCloudSnapshot()
    {
        _cloudSnapshot.Clear();
        _cloudSnapshotTimestamp = null;
    }

    // ── PlayerPrefs persistence ───────────────────────────────────────────────

    /// <summary>
    /// Ensures a one-time load from PlayerPrefs on first data access.
    /// Must run on the main thread — PlayerPrefs are unavailable from background threads.
    /// </summary>
    private void EnsureLocalLoaded()
    {
        if (_localLoaded) return;
        _localLoaded = true;
        LoadLocalFromPrefs();
    }

    private void LoadLocalFromPrefs()
    {
        var json = PlayerPrefs.GetString(_localPrefsKey, "{}");
        try { _local = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new(); }
        catch { _local = new(); }

        LocalTimestamp = TryParsePrefsTimestamp(_localTimestampPrefsKey);
        BaseTimestamp  = TryParsePrefsTimestamp(_baseTimestampPrefsKey);

        // Upgrade path: older builds only stored local ts. Treat as clean at that version
        // so the next Set() marks dirty correctly against BaseTimestamp.
        if (!BaseTimestamp.HasValue && LocalTimestamp.HasValue)
        {
            BaseTimestamp = LocalTimestamp;
            PersistTimestampsToPrefs();
        }
    }

    static DateTime? TryParsePrefsTimestamp(string prefsKey)
    {
        var tsStr = PlayerPrefs.GetString(prefsKey, "");
        return DateTime.TryParse(
            tsStr, null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var ts)
            ? ts
            : null;
    }

    private void PersistLocalToPrefs()
    {
        PlayerPrefs.SetString(_localPrefsKey, JsonConvert.SerializeObject(_local));
        PersistTimestampsToPrefs();
        PlayerPrefs.Save();
    }

    private void PersistTimestampsToPrefs()
    {
        if (LocalTimestamp.HasValue)
            PlayerPrefs.SetString(_localTimestampPrefsKey, LocalTimestamp.Value.ToString("O"));
        if (BaseTimestamp.HasValue)
            PlayerPrefs.SetString(_baseTimestampPrefsKey, BaseTimestamp.Value.ToString("O"));
        PlayerPrefs.Save();
    }
}
