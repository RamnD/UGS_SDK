using System;
using System.Collections.Generic;
using Unity.Services.Economy.Model;
using UnityEngine;

/// <summary>
/// Persistent balance cache in PlayerPrefs.
/// Stores last known values for offline access and fast UI startup.
/// <para>
/// Used inside <see cref="UGSEconomyService{TCurrency}"/> — not intended for
/// direct use from game systems. Access balances via your game bridge (e.g. MonoBehaviour + <see cref="IInventoryService{TCurrency}"/>).
/// </para>
/// </summary>
internal sealed class BalanceCache<TCurrency> where TCurrency : struct, Enum
{
    private const string PrefsKey = "economy_cached_balances";

    private readonly Dictionary<TCurrency, long> _data = new();

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>Returns cached balance. 0 if no data.</summary>
    public long Get(TCurrency type) => _data.TryGetValue(type, out var val) ? val : 0L;

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Updates one in-memory value (no disk write).</summary>
    public void Set(TCurrency type, long value) => _data[type] = value;

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the full in-memory cache to PlayerPrefs.
    /// Call after every server response and after offline operations.
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
    /// Loads cache from PlayerPrefs into memory.
    /// Call at session start (offline boot) or during RefreshBalancesAsync in offline mode.
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
    /// Updates cache from a server response and saves to disk immediately.
    /// Call after every successful GetBalancesAsync().
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

    /// <summary>Logs all cached balances to the Console.</summary>
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
