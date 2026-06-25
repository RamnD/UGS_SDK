using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using Unity.Services.Economy.Model;
using UnityEngine;

/// <summary>
/// Реализация <see cref="IConsumableItemService{TItem}"/> через UGS Economy PlayerBalances.
/// Consumables в Dashboard задаются как <b>Currency</b>; quantity = баланс валюты.
/// </summary>
public sealed class UGSConsumableItemService<TItem> : IConsumableItemService<TItem>
    where TItem : struct, Enum
{
    private const string CachePrefsKey = "consumables_currency_cache";

    private readonly IConsumableItemMapper<TItem> _mapper;
    private readonly Dictionary<TItem, int> _quantities = new();

    public event Action<TItem, int> OnQuantityChanged;

    public UGSConsumableItemService(IConsumableItemMapper<TItem> mapper)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    /// <inheritdoc/>
    public int GetQuantity(TItem id) =>
        _quantities.TryGetValue(id, out var qty) ? qty : 0;

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!NetworkStatus.IsOnline)
        {
            LoadFromPrefs();
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EconomyService.Instance.PlayerBalances.GetBalancesAsync();
            cancellationToken.ThrowIfCancellationRequested();

            RebuildFromBalances(result.Balances);
            SaveToPrefs();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InventoryOperationException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Consumables] Balance sync failed: {e.Message}");
            throw new InventoryOperationException(
                InventoryFailureReason.ProviderRejected,
                "Failed to synchronize consumable balances.",
                e);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryConsumeAsync(
        TItem id,
        int amount = 1,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            return false;

        if (!_mapper.IsConsumable(id))
            return false;

        if (GetQuantity(id) < amount)
        {
            if (!NetworkStatus.IsOnline)
                return false;

            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (InventoryOperationException)
            {
                return false;
            }

            if (GetQuantity(id) < amount)
                return false;
        }

        if (!NetworkStatus.IsOnline)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EconomyService.Instance.PlayerBalances.DecrementBalanceAsync(
                _mapper.ToServiceId(id),
                amount);
            cancellationToken.ThrowIfCancellationRequested();

            SetQuantity(id, ToIntQuantity(result.Balance));
            SaveToPrefs();
            RaiseChanged(id);
            return true;
        }
        catch (EconomyException e) when (e.Reason == EconomyExceptionReason.UnprocessableTransaction)
        {
            Debug.LogWarning($"[Consumables] Insufficient {id} per server — refreshing cache.");
            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (Exception refreshEx)
            {
                Debug.LogWarning($"[Consumables] Re-sync after insufficient funds: {refreshEx.Message}");
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Consumables] Consume failed for {id}: {e.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryGrantAsync(
        TItem id,
        int amount = 1,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            return false;

        if (!_mapper.IsConsumable(id))
            return false;

        if (!NetworkStatus.IsOnline)
        {
            if (!_mapper.IsOfflineAllowed(id, InventoryOperation.Add))
                return false;

            SetQuantity(id, GetQuantity(id) + amount);
            SaveToPrefs();
            RaiseChanged(id);
            return true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EconomyService.Instance.PlayerBalances.IncrementBalanceAsync(
                _mapper.ToServiceId(id),
                amount);
            cancellationToken.ThrowIfCancellationRequested();

            SetQuantity(id, ToIntQuantity(result.Balance));
            SaveToPrefs();
            RaiseChanged(id);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Consumables] Grant failed for {id}: {e.Message}");
            return false;
        }
    }

    private void RebuildFromBalances(IReadOnlyList<PlayerBalance> balances)
    {
        _quantities.Clear();

        if (balances == null)
            return;

        foreach (TItem id in Enum.GetValues(typeof(TItem)))
        {
            if (!_mapper.IsConsumable(id))
                continue;

            var currencyId = _mapper.ToServiceId(id);
            var balance = balances.FirstOrDefault(b => b.CurrencyId == currencyId);
            var qty = ToIntQuantity(balance?.Balance ?? 0);
            if (qty > 0)
                _quantities[id] = qty;
        }
    }

    private void SetQuantity(TItem id, int quantity)
    {
        if (quantity <= 0)
            _quantities.Remove(id);
        else
            _quantities[id] = quantity;
    }

    private static int ToIntQuantity(long balance) =>
        (int)Math.Min(int.MaxValue, Math.Max(0, balance));

    private void RaiseChanged(TItem id) =>
        OnQuantityChanged?.Invoke(id, GetQuantity(id));

    private void SaveToPrefs()
    {
        var cache = new QuantityCache();
        foreach (var pair in _quantities)
        {
            cache.entries.Add(new QuantityEntry
            {
                item = pair.Key.ToString(),
                quantity = pair.Value
            });
        }

        PlayerPrefs.SetString(CachePrefsKey, JsonUtility.ToJson(cache));
        PlayerPrefs.Save();
    }

    private void LoadFromPrefs()
    {
        _quantities.Clear();

        var json = PlayerPrefs.GetString(CachePrefsKey, "{}");
        var cache = JsonUtility.FromJson<QuantityCache>(json) ?? new QuantityCache();
        foreach (var entry in cache.entries)
        {
            if (!Enum.TryParse<TItem>(entry.item, out var id))
                continue;
            if (entry.quantity > 0)
                _quantities[id] = entry.quantity;
        }
    }

    [Serializable]
    private class QuantityCache
    {
        public List<QuantityEntry> entries = new();
    }

    [Serializable]
    private class QuantityEntry
    {
        public string item;
        public int quantity;
    }
}
