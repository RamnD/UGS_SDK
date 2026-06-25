using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// Реализация <see cref="IItemService{TItem}"/> через Unity Gaming Services Economy PlayerInventory.
/// </summary>
public sealed class UGSItemService<TItem, TCurrency> : IItemService<TItem>
    where TItem     : struct, Enum
    where TCurrency : struct, Enum
{
    private const string CachePrefsKey = "items_owned_cache";

    private readonly IItemMapper<TItem, TCurrency>  _mapper;
    private readonly IInventoryService<TCurrency>   _economy;
    private readonly HashSet<TItem>                 _ownedItems = new();

    public UGSItemService(IItemMapper<TItem, TCurrency> mapper, IInventoryService<TCurrency> economy)
    {
        _mapper  = mapper  ?? throw new ArgumentNullException(nameof(mapper));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
    }

    /// <inheritdoc/>
    public bool IsOwned(TItem id) => _ownedItems.Contains(id);

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
            var result = await EconomyService.Instance.PlayerInventory.GetInventoryAsync();
            cancellationToken.ThrowIfCancellationRequested();

            _ownedItems.Clear();

            foreach (TItem id in Enum.GetValues(typeof(TItem)))
            {
                if (result.PlayersInventoryItems.Exists(i => i.InventoryItemId == _mapper.ToServiceId(id)))
                    _ownedItems.Add(id);
            }

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
            Debug.LogError($"[Items] Inventory load failed: {e.Message}");
            throw new InventoryOperationException(
                InventoryFailureReason.ProviderRejected,
                "Failed to synchronize inventory.",
                e);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryPurchaseAsync(TItem id, CancellationToken cancellationToken = default)
    {
        if (IsOwned(id)) return true;

        if (!NetworkStatus.IsOnline)
        {
            throw new InventoryOperationException(
                InventoryFailureReason.NetworkUnavailable,
                "Purchase requires network.");
        }

        TCurrency costCurrency = _mapper.GetCostCurrency(id);
        int       cost         = _mapper.GetCost(id);

        bool paid;
        try
        {
            paid = await _economy.TrySpendCurrencyAsync(costCurrency, cost, cancellationToken);
        }
        catch (InventoryOperationException)
        {
            throw;
        }

        if (!paid) return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EconomyService.Instance.PlayerInventory.AddInventoryItemAsync(_mapper.ToServiceId(id));
            cancellationToken.ThrowIfCancellationRequested();

            _ownedItems.Add(id);
            SaveToPrefs();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Items] Grant failed for {id}, rolling back {cost} {costCurrency}: {e.Message}");
            try
            {
                await _economy.AddCurrencyAsync(costCurrency, cost, cancellationToken);
            }
            catch (InventoryOperationException)
            {
                Debug.LogError("[Items] Currency rollback after item grant failure was incomplete.");
            }

            return false;
        }
    }

    private void SaveToPrefs()
    {
        var cache = new ItemCache();
        foreach (var id in _ownedItems)
            cache.items.Add(id.ToString());
        PlayerPrefs.SetString(CachePrefsKey, JsonUtility.ToJson(cache));
        PlayerPrefs.Save();
    }

    private void LoadFromPrefs()
    {
        var json  = PlayerPrefs.GetString(CachePrefsKey, "{}");
        var cache = JsonUtility.FromJson<ItemCache>(json) ?? new ItemCache();
        _ownedItems.Clear();
        foreach (var entry in cache.items)
        {
            if (Enum.TryParse<TItem>(entry, out var id))
                _ownedItems.Add(id);
        }
    }

    [Serializable] private class ItemCache { public List<string> items = new(); }
}
