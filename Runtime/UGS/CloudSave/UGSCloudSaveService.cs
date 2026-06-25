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
/// Timestamp is stored as special key <c>__ts</c> in the cloud and separately in PlayerPrefs.
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

    private Dictionary<string, string> _local        = new();
    private Dictionary<string, string> _cloudSnapshot = new(); // pending conflict snapshot
    private DateTime?                  _cloudSnapshotTimestamp;
    private bool                       _localLoaded;

    /// <inheritdoc/>
    public DateTime? LocalTimestamp { get; private set; }

    /// <param name="mapper">Key mapper: enum → cloud string.</param>
    public UGSCloudSaveService(ISaveKeyMapper<TKey> mapper)
    {
        _mapper                 = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _localPrefsKey          = $"cloud_save_{typeof(TKey).Name}_local";
        _localTimestampPrefsKey = $"cloud_save_{typeof(TKey).Name}_ts";
        // PlayerPrefs are main-thread only — do NOT load here.
        // Loading is lazy on first data access (EnsureLocalLoaded).
    }

    // ── Local access ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public TValue Get<TValue>(TKey key, TValue defaultValue = default)
    {
        EnsureLocalLoaded();
        if (!_local.TryGetValue(_mapper.ToCloudKey(key), out var json))
            return defaultValue;
        try   { return JsonConvert.DeserializeObject<TValue>(json); }
        catch { return defaultValue; }
    }

    /// <inheritdoc/>
    public void Set<TValue>(TKey key, TValue value)
    {
        EnsureLocalLoaded();
        _local[_mapper.ToCloudKey(key)] = JsonConvert.SerializeObject(value);
        LocalTimestamp = DateTime.UtcNow;
        PersistLocalToPrefs();
    }

    // ── Cloud sync ────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SaveConflict?> LoadAsync(CancellationToken cancellationToken = default)
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

            // Read cloud snapshot
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

            Debug.Log($"[CloudSave] Loaded from cloud: {_cloudSnapshot.Count} keys, ts={_cloudSnapshotTimestamp:O}");
            foreach (var kvp in _cloudSnapshot)
                Debug.Log($"  [{kvp.Key}] = {kvp.Value}");

            // Cloud has only __ts and no data — keep local data
            if (_cloudSnapshot.Count == 0)
            {
                Debug.Log("[CloudSave] Cloud has only timestamp payload — keeping local save.");
                return null;
            }

            // No local data → apply cloud without conflict
            if (!LocalTimestamp.HasValue)
            {
                ApplyCloud();
                return null;
            }

            // Timestamps match → data in sync
            if (_cloudSnapshotTimestamp.HasValue &&
                Math.Abs((_cloudSnapshotTimestamp.Value - LocalTimestamp.Value).TotalSeconds) < 1)
            {
                return null;
            }

            // Conflict — return for user resolution
            return new SaveConflict(
                LocalTimestamp.Value,
                _cloudSnapshotTimestamp ?? DateTime.MinValue);
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
    public async Task PushToCloudAsync(CancellationToken cancellationToken = default)
    {
        EnsureLocalLoaded();
        if (!NetworkStatus.IsOnline) return;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ts = DateTime.UtcNow;
            var cloudData = new Dictionary<string, object>();

            foreach (var kvp in _local)
                cloudData[kvp.Key] = kvp.Value;

            cloudData[TimestampCloudKey] = ts.ToString("O");

            Debug.Log($"[CloudSave] Pushing to cloud: {_local.Count} keys + __ts");
            foreach (var kvp in _local)
                Debug.Log($"  [{kvp.Key}] = {kvp.Value}");

            await CloudSaveService.Instance.Data.Player.SaveAsync(cloudData);
            cancellationToken.ThrowIfCancellationRequested();

            LocalTimestamp = ts;
            PersistTimestampToPrefs();
            Debug.Log($"[CloudSave] Saved to cloud. Timestamp: {ts:O}");
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
        _local                  = new Dictionary<string, string>(_cloudSnapshot);
        LocalTimestamp          = _cloudSnapshotTimestamp;
        _cloudSnapshot.Clear();
        _cloudSnapshotTimestamp = null;
        PersistLocalToPrefs();
        Debug.Log("[CloudSave] Applied cloud snapshot.");
    }

    /// <inheritdoc/>
    public void KeepLocal()
    {
        _cloudSnapshot.Clear();
        _cloudSnapshotTimestamp = null;
        Debug.Log("[CloudSave] Kept local save.");
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

        var tsStr = PlayerPrefs.GetString(_localTimestampPrefsKey, "");
        LocalTimestamp = DateTime.TryParse(
            tsStr, null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var ts) ? ts : (DateTime?)null;
    }

    private void PersistLocalToPrefs()
    {
        PlayerPrefs.SetString(_localPrefsKey, JsonConvert.SerializeObject(_local));
        PersistTimestampToPrefs();
        PlayerPrefs.Save();
    }

    private void PersistTimestampToPrefs()
    {
        if (LocalTimestamp.HasValue)
            PlayerPrefs.SetString(_localTimestampPrefsKey, LocalTimestamp.Value.ToString("O"));
    }
}
