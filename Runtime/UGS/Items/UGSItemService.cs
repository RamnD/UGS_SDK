using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// <see cref="IItemService{TItem}"/> implementation via Unity Gaming Services Economy PlayerInventory.
/// </summary>
public sealed class UGSItemService<TItem, TCurrency> : IItemService<TItem>
    where TItem     : struct, Enum
    where TCurrency : struct, Enum
{
    private readonly string _cachePrefsKey;
    private readonly IItemMapper<TItem, TCurrency>  _mapper;
    private readonly IInventoryService<TCurrency>   _economy;
    private readonly HashSet<TItem>                 _ownedItems = new();
    private readonly object                         _refreshGate = new object();
    private Task                                    _refreshTask;

    public UGSItemService(IItemMapper<TItem, TCurrency> mapper, IInventoryService<TCurrency> economy)
    {
        _mapper  = mapper  ?? throw new ArgumentNullException(nameof(mapper));
        _economy = economy ?? throw new ArgumentNullException(nameof(economy));
        _cachePrefsKey = $"items_owned_cache_{typeof(TItem).Name}";
        MigrateLegacyCacheKeyIfNeeded();
        LoadFromPrefs();
    }

    /// <inheritdoc/>
    public bool IsOwned(TItem id) => _ownedItems.Contains(id);

    /// <inheritdoc/>
    public void ClearLocalCache()
    {
        _ownedItems.Clear();
        if (PlayerPrefs.HasKey(_cachePrefsKey))
            PlayerPrefs.DeleteKey(_cachePrefsKey);
        PlayerPrefs.Save();
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Task refresh;
        lock (_refreshGate)
        {
            if (_refreshTask != null && !_refreshTask.IsCompleted)
                refresh = _refreshTask;
            else
            {
                refresh = RefreshCoreAsync(cancellationToken);
                _refreshTask = refresh;
            }
        }

        try
        {
            await refresh;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_refreshGate)
            {
                if (ReferenceEquals(_refreshTask, refresh) && refresh.IsCompleted)
                    _refreshTask = null;
            }
        }
    }

    async Task RefreshCoreAsync(CancellationToken cancellationToken)
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

        bool granted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EconomyService.Instance.PlayerInventory.AddInventoryItemAsync(_mapper.ToServiceId(id));
            granted = true;

            _ownedItems.Add(id);
            SaveToPrefs();
            return true;
        }
        catch (OperationCanceledException)
        {
            // Grant may have landed on the server even if the client was cancelled.
            if (!granted)
                await TryConfirmGrantOrRefundAsync(id, costCurrency, cost);

            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Items] Grant failed for {id}, rolling back {cost} {costCurrency}: {e.Message}");
            if (!granted)
                await RefundCurrencyAsync(costCurrency, cost);

            return false;
        }
    }

    /// <summary>
    /// After cancel during grant: refresh ownership — if owned, keep (no refund); else refund.
    /// </summary>
    private async Task TryConfirmGrantOrRefundAsync(TItem id, TCurrency costCurrency, int cost)
    {
        try
        {
            await RefreshAsync(CancellationToken.None);
        }
        catch (Exception refreshEx)
        {
            Debug.LogWarning($"[Items] Post-cancel ownership refresh failed: {refreshEx.Message}");
        }

        if (IsOwned(id))
        {
            Debug.Log($"[Items] Grant confirmed after cancel for {id} — no currency refund.");
            return;
        }

        await RefundCurrencyAsync(costCurrency, cost);
    }

    private async Task RefundCurrencyAsync(TCurrency costCurrency, int cost)
    {
        try
        {
            await _economy.AddCurrencyAsync(costCurrency, cost, CancellationToken.None);
        }
        catch (InventoryOperationException)
        {
            Debug.LogError("[Items] Currency rollback after item grant failure was incomplete.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Items] Currency rollback failed: {ex.Message}");
        }
    }

    private void SaveToPrefs()
    {
        var cache = new ItemCache();
        foreach (var id in _ownedItems)
            cache.items.Add(id.ToString());
        PlayerPrefs.SetString(_cachePrefsKey, JsonUtility.ToJson(cache));
        PlayerPrefs.Save();
    }

    private void LoadFromPrefs()
    {
        var json  = PlayerPrefs.GetString(_cachePrefsKey, "{}");
        var cache = JsonUtility.FromJson<ItemCache>(json) ?? new ItemCache();
        cache.items ??= new List<string>();
        _ownedItems.Clear();
        foreach (var entry in cache.items)
        {
            if (Enum.TryParse<TItem>(entry, out var id))
                _ownedItems.Add(id);
        }
    }

    void MigrateLegacyCacheKeyIfNeeded()
    {
        const string legacyKey = "items_owned_cache";
        if (PlayerPrefs.HasKey(_cachePrefsKey) || !PlayerPrefs.HasKey(legacyKey))
            return;

        PlayerPrefs.SetString(_cachePrefsKey, PlayerPrefs.GetString(legacyKey, "{}"));
        PlayerPrefs.DeleteKey(legacyKey);
        PlayerPrefs.Save();
        Debug.Log($"[Items] Migrated cache key → {_cachePrefsKey}.");
    }

    [Serializable] private class ItemCache { public List<string> items = new(); }
}
