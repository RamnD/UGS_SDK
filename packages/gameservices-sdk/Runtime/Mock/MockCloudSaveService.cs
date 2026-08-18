using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Mock <see cref="ICloudSaveService{TKey}"/> implementation.
/// Stores data in memory only — no network and no PlayerPrefs.
/// <para>
/// Does not create conflicts (always returns null from Load/Push).
/// Use for UI and logic development before setting up UGS Cloud Save.
/// </para>
/// Usage:
/// <code>
/// var save = new MockCloudSaveService&lt;SaveKey&gt;(new SaveKeyMapper());
/// save.Set(SaveKey.HighScore, 1234L);
/// PlayerSaveData.Instance.Init(save);
/// </code>
/// </summary>
public sealed class MockCloudSaveService<TKey> : ICloudSaveService<TKey>
    where TKey : struct, Enum
{
    private readonly ISaveKeyMapper<TKey> _mapper;
    private readonly Dictionary<string, string> _data = new();

    /// <inheritdoc/>
    public DateTime? LocalTimestamp { get; private set; }

    /// <inheritdoc/>
    public DateTime? BaseTimestamp { get; private set; }

    public MockCloudSaveService(ISaveKeyMapper<TKey> mapper)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc/>
    public TValue Get<TValue>(TKey key, TValue defaultValue = default)
    {
        return TryGet(key, out TValue value) ? value : defaultValue;
    }

    /// <inheritdoc/>
    public bool TryGet<TValue>(TKey key, out TValue value)
    {
        value = default;
        if (!_data.TryGetValue(_mapper.ToCloudKey(key), out var json))
            return false;

        try
        {
            value = JsonConvert.DeserializeObject<TValue>(json);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("MockCloudSave", $"Corrupt value for key '{key}' — leaving raw JSON intact. {ex.Message}");
            throw new InvalidOperationException(
                $"Cloud save key '{key}' contains corrupt data and cannot be read safely.",
                ex);
        }
    }

    /// <inheritdoc/>
    public void Set<TValue>(TKey key, TValue value)
    {
        _data[_mapper.ToCloudKey(key)] = JsonConvert.SerializeObject(value);
        LocalTimestamp = DateTime.UtcNow;
        AppLog.DebugLog("MockCloudSave", $"Set {key} = {value}");
    }

    /// <inheritdoc/>
    public void ClearLocalCache()
    {
        _data.Clear();
        LocalTimestamp = null;
        BaseTimestamp = null;
        AppLog.DebugLog("MockCloudSave", "ClearLocalCache.");
    }

    /// <inheritdoc/>
    public Task<SaveConflict?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.DebugLog("MockCloudSave", "LoadAsync (mock, no conflicts).");
        return Task.FromResult<SaveConflict?>(null);
    }

    /// <inheritdoc/>
    public Task<SaveConflict?> PushToCloudAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ts = DateTime.UtcNow;
        LocalTimestamp = ts;
        BaseTimestamp  = ts;
        AppLog.DebugLog("MockCloudSave", "PushToCloud (mock, nothing sent).");
        return Task.FromResult<SaveConflict?>(null);
    }

    /// <inheritdoc/>
    public void ApplyCloud() =>
        AppLog.DebugLog("MockCloudSave", "ApplyCloud (mock, nothing to apply).");

    /// <inheritdoc/>
    public void KeepLocal() =>
        AppLog.DebugLog("MockCloudSave", "KeepLocal (mock).");
}
