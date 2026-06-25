using System;
using System.Collections.Generic;
using Unity.Services.Economy.Model;
using UnityEngine;

/// <summary>
/// Персистентный кэш балансов в PlayerPrefs.
/// Хранит последние известные значения для офлайн-доступа и быстрого старта UI.
/// <para>
/// Используется внутри <see cref="UGSEconomyService{TCurrency}"/> — не предназначен для
/// прямого использования из игровых систем. Доступ к балансам — через ваш игровой мост (например MonoBehaviour + <see cref="IInventoryService{TCurrency}"/>).
/// </para>
/// </summary>
internal sealed class BalanceCache<TCurrency> where TCurrency : struct, Enum
{
    private const string PrefsKey = "economy_cached_balances";

    private readonly Dictionary<TCurrency, long> _data = new();

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>Возвращает кэшированный баланс. 0 если данных нет.</summary>
    public long Get(TCurrency type) => _data.TryGetValue(type, out var val) ? val : 0L;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Обновляет одно значение в памяти (без записи на диск).</summary>
    public void Set(TCurrency type, long value) => _data[type] = value;

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>
    /// Записывает весь кэш из памяти в PlayerPrefs.
    /// Вызывать после каждого серверного ответа и после офлайн-операций.
    /// </summary>
    public void Save()
    {
        var payload = new CachePayload();
        foreach (var kvp in _data)
            payload.entries.Add(new CacheEntry { currency = kvp.Key.ToString(), balance = kvp.Value });
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(payload));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Загружает кэш из PlayerPrefs в память.
    /// Вызывать в начале сессии (офлайн-старт) или при RefreshBalancesAsync в офлайн-режиме.
    /// </summary>
    public void Load()
    {
        var json    = PlayerPrefs.GetString(PrefsKey, "{}");
        var payload = JsonUtility.FromJson<CachePayload>(json) ?? new CachePayload();
        foreach (var entry in payload.entries)
        {
            if (Enum.TryParse<TCurrency>(entry.currency, out var type))
                _data[type] = entry.balance;
        }
    }

    /// <summary>
    /// Обновляет кэш из серверного ответа и немедленно сохраняет на диск.
    /// Вызывать после каждого успешного GetBalancesAsync().
    /// </summary>
    public void UpdateFromServer(List<PlayerBalance> serverBalances, ICurrencyMapper<TCurrency> mapper)
    {
        foreach (TCurrency type in Enum.GetValues(typeof(TCurrency)))
        {
            var item = serverBalances.Find(b => b.CurrencyId == mapper.ToServiceId(type));
            _data[type] = item?.Balance ?? 0L;
        }
        Save();
    }

    /// <summary>Выводит все кэшированные балансы в Console.</summary>
    public void LogAll()
    {
        var sb = new System.Text.StringBuilder("[Economy] Balances after sync:\n");
        foreach (var kvp in _data)
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        Debug.Log(sb.ToString());
    }

    // ── Serialization ─────────────────────────────────────────────────────────

    [Serializable] private class CacheEntry   { public string currency; public long balance; }
    [Serializable] private class CachePayload { public List<CacheEntry> entries = new(); }
}
