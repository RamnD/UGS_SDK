using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Mock-реализация <see cref="ICloudSaveService{TKey}"/>.
/// Хранит данные только в памяти — без сети и без PlayerPrefs.
/// <para>
/// Конфликтов не создаёт (всегда возвращает null из <see cref="LoadAsync"/>).
/// Используйте для разработки UI и логики до настройки UGS Cloud Save.
/// </para>
/// Использование:
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

    public MockCloudSaveService(ISaveKeyMapper<TKey> mapper)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc/>
    public TValue Get<TValue>(TKey key, TValue defaultValue = default)
    {
        if (!_data.TryGetValue(_mapper.ToCloudKey(key), out var json))
            return defaultValue;
        try   { return JsonConvert.DeserializeObject<TValue>(json); }
        catch { return defaultValue; }
    }

    /// <inheritdoc/>
    public void Set<TValue>(TKey key, TValue value)
    {
        _data[_mapper.ToCloudKey(key)] = JsonConvert.SerializeObject(value);
        LocalTimestamp = DateTime.UtcNow;
        Debug.Log($"[Mock CloudSave] Set {key} = {value}");
    }

    /// <inheritdoc/>
    public Task<SaveConflict?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Debug.Log("[Mock CloudSave] LoadAsync (mock, no conflicts).");
        return Task.FromResult<SaveConflict?>(null);
    }

    /// <inheritdoc/>
    public Task PushToCloudAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LocalTimestamp = DateTime.UtcNow;
        Debug.Log("[Mock CloudSave] PushToCloud (mock, nothing sent).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void ApplyCloud() =>
        Debug.Log("[Mock CloudSave] ApplyCloud (mock, nothing to apply).");

    /// <inheritdoc/>
    public void KeepLocal() =>
        Debug.Log("[Mock CloudSave] KeepLocal (mock).");
}
