using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.CloudSave;
using UnityEngine;

/// <summary>
/// Реализация <see cref="ICloudSaveService{TKey}"/> через Unity Gaming Services Cloud Save SDK 3.x.
/// <para>
/// <b>Хранение:</b> локальный кэш — PlayerPrefs (JSON), облако — UGS Cloud Save (Player Data).
/// Временна́я метка хранится как специальный ключ <c>__ts</c> в облаке и отдельно в PlayerPrefs.
/// </para>
/// <para>
/// <b>Сериализация:</b> все значения сериализуются в JSON через Newtonsoft.Json.
/// Работает с примитивами (int, long, bool, string) и сериализуемыми классами/структурами.
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

    /// <param name="mapper">Маппер ключей: enum → строка для облака.</param>
    public UGSCloudSaveService(ISaveKeyMapper<TKey> mapper)
    {
        _mapper                 = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _localPrefsKey          = $"cloud_save_{typeof(TKey).Name}_local";
        _localTimestampPrefsKey = $"cloud_save_{typeof(TKey).Name}_ts";
        // PlayerPrefs доступны только с main thread — НЕ загружаем здесь.
        // Загрузка выполняется лениво при первом обращении к данным (EnsureLocalLoaded).
    }

    // ── Локальный доступ ──────────────────────────────────────────────────────

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

    // ── Облачная синхронизация ────────────────────────────────────────────────

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
                return null; // облако пустое — оставляем локальные данные

            // Читаем облачный снимок
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

            // Облако содержит только __ts без данных — оставляем локальные данные
            if (_cloudSnapshot.Count == 0)
            {
                Debug.Log("[CloudSave] Cloud has only timestamp payload — keeping local save.");
                return null;
            }

            // Нет локальных данных → применяем облако без конфликта
            if (!LocalTimestamp.HasValue)
            {
                ApplyCloud();
                return null;
            }

            // Временны́е метки совпадают → данные синхронизированы
            if (_cloudSnapshotTimestamp.HasValue &&
                Math.Abs((_cloudSnapshotTimestamp.Value - LocalTimestamp.Value).TotalSeconds) < 1)
            {
                return null;
            }

            // Конфликт — возвращаем для разрешения пользователем
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
    /// Гарантирует однократную загрузку из PlayerPrefs при первом обращении к данным.
    /// Должна вызываться с main thread — PlayerPrefs недоступны из фоновых потоков.
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
