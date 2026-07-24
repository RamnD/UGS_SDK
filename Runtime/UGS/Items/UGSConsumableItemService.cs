using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using Unity.Services.Economy.Model;
using UnityEngine;

/// <summary>
/// <see cref="IConsumableItemService{TItem}"/> implementation via UGS Economy PlayerBalances.
/// Consumables in the Dashboard are defined as <b>Currency</b>; quantity = currency balance.
/// Offline / recoverable grants use a durable pending queue reapplied after server rebuild.
/// </summary>
public sealed class UGSConsumableItemService<TItem> : IConsumableItemService<TItem>
    where TItem : struct, Enum
{
    readonly string _cachePrefsKey;
    readonly string _pendingPrefsKey;

    readonly IConsumableItemMapper<TItem> _mapper;
    readonly Dictionary<TItem, int> _quantities = new();
    readonly object _pendingSync = new object();

    public event Action<TItem, int> OnQuantityChanged;

    public UGSConsumableItemService(IConsumableItemMapper<TItem> mapper)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        string typeName = typeof(TItem).Name;
        _cachePrefsKey = $"consumables_currency_cache_{typeName}";
        _pendingPrefsKey = $"consumables_pending_grants_{typeName}";
        MigrateLegacyCacheKeyIfNeeded(typeName);
        LoadFromPrefs();
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
            await FlushPendingGrantsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await EconomyService.Instance.PlayerBalances.GetBalancesAsync();
            cancellationToken.ThrowIfCancellationRequested();

            RebuildFromBalances(result.Balances);
            ApplyPendingOnTop();
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

            ApplyLocalGrant(id, amount);
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
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e)
                                  && _mapper.IsOfflineAllowed(id, InventoryOperation.Add))
        {
            Debug.LogWarning($"[Consumables] Grant {id} failed (recoverable) — queued locally: {e.Message}");
            ApplyLocalGrant(id, amount);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Consumables] Grant failed for {id}: {e.Message}");
            return false;
        }
    }

    void ApplyLocalGrant(TItem id, int amount)
    {
        SetQuantity(id, GetQuantity(id) + amount);
        EnqueuePending(id, amount);
        SaveToPrefs();
        RaiseChanged(id);
    }

    async Task FlushPendingGrantsAsync(CancellationToken cancellationToken)
    {
        List<PendingGrant> work;
        lock (_pendingSync)
        {
            var queue = LoadPendingUnlocked();
            if (queue.items.Count == 0)
                return;
            work = new List<PendingGrant>(queue.items);
        }

        Debug.Log($"[Consumables] Flushing {work.Count} pending grant(s).");

        foreach (PendingGrant grant in work)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (grant.amount <= 0 || !Enum.TryParse(grant.item, out TItem id) || !_mapper.IsConsumable(id))
            {
                RemovePending(grant.item);
                continue;
            }

            try
            {
                var result = await EconomyService.Instance.PlayerBalances.IncrementBalanceAsync(
                    _mapper.ToServiceId(id),
                    grant.amount);
                SetQuantity(id, ToIntQuantity(result.Balance));
                RemovePending(grant.item);
                RaiseChanged(id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
            {
                Debug.LogWarning(
                    $"[Consumables] Pending grant flush paused ({grant.item} +{grant.amount}): {e.Message}");
                return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Consumables] Pending grant flush failed ({grant.item}): {e.Message}");
                throw new InventoryOperationException(
                    InventoryFailureReason.PendingTransactionsFlushFailed,
                    "Failed to upload pending consumable grants.",
                    e);
            }
        }
    }

    void RebuildFromBalances(IReadOnlyList<PlayerBalance> balances)
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

    /// <summary>Re-apply unflushed pending grants on top of server balances so offline grants survive refresh.</summary>
    void ApplyPendingOnTop()
    {
        lock (_pendingSync)
        {
            var queue = LoadPendingUnlocked();
            foreach (PendingGrant grant in queue.items)
            {
                if (grant.amount <= 0 || !Enum.TryParse(grant.item, out TItem id))
                    continue;
                SetQuantity(id, GetQuantity(id) + grant.amount);
            }
        }
    }

    void EnqueuePending(TItem id, int amount)
    {
        lock (_pendingSync)
        {
            var queue = LoadPendingUnlocked();
            string key = id.ToString();
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingGrant existing = queue.items[i];
                if (!string.Equals(existing.item, key, StringComparison.Ordinal))
                    continue;

                long net = (long)existing.amount + amount;
                if (net > int.MaxValue)
                {
                    Debug.LogError($"[Consumables] Pending grant overflow for {key}.");
                    return;
                }

                existing.amount = (int)net;
                queue.items[i] = existing;
                PersistPendingUnlocked(queue);
                return;
            }

            queue.items.Add(new PendingGrant { item = key, amount = amount });
            PersistPendingUnlocked(queue);
        }
    }

    void RemovePending(string itemKey)
    {
        if (string.IsNullOrEmpty(itemKey))
            return;

        lock (_pendingSync)
        {
            var queue = LoadPendingUnlocked();
            for (int i = queue.items.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(queue.items[i].item, itemKey, StringComparison.Ordinal))
                    continue;
                queue.items.RemoveAt(i);
                PersistPendingUnlocked(queue);
                return;
            }
        }
    }

    PendingGrantQueue LoadPendingUnlocked()
    {
        string json = PlayerPrefs.GetString(_pendingPrefsKey, "{}");
        var queue = JsonUtility.FromJson<PendingGrantQueue>(json) ?? new PendingGrantQueue();
        queue.items ??= new List<PendingGrant>();
        return queue;
    }

    void PersistPendingUnlocked(PendingGrantQueue queue)
    {
        if (queue.items == null || queue.items.Count == 0)
            PlayerPrefs.DeleteKey(_pendingPrefsKey);
        else
            PlayerPrefs.SetString(_pendingPrefsKey, JsonUtility.ToJson(queue));
        PlayerPrefs.Save();
    }

    void SetQuantity(TItem id, int quantity)
    {
        if (quantity <= 0)
            _quantities.Remove(id);
        else
            _quantities[id] = quantity;
    }

    static int ToIntQuantity(long balance) =>
        (int)Math.Min(int.MaxValue, Math.Max(0, balance));

    void RaiseChanged(TItem id) =>
        OnQuantityChanged?.Invoke(id, GetQuantity(id));

    void SaveToPrefs()
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

        PlayerPrefs.SetString(_cachePrefsKey, JsonUtility.ToJson(cache));
        PlayerPrefs.Save();
    }

    void LoadFromPrefs()
    {
        _quantities.Clear();

        var json = PlayerPrefs.GetString(_cachePrefsKey, "{}");
        var cache = JsonUtility.FromJson<QuantityCache>(json) ?? new QuantityCache();
        cache.entries ??= new List<QuantityEntry>();
        foreach (var entry in cache.entries)
        {
            if (!Enum.TryParse<TItem>(entry.item, out var id))
                continue;
            if (entry.quantity > 0)
                _quantities[id] = entry.quantity;
        }
    }

    void MigrateLegacyCacheKeyIfNeeded(string typeName)
    {
        const string legacyKey = "consumables_currency_cache";
        if (PlayerPrefs.HasKey(_cachePrefsKey) || !PlayerPrefs.HasKey(legacyKey))
            return;

        PlayerPrefs.SetString(_cachePrefsKey, PlayerPrefs.GetString(legacyKey, "{}"));
        PlayerPrefs.DeleteKey(legacyKey);
        PlayerPrefs.Save();
        Debug.Log($"[Consumables] Migrated cache key → consumables_currency_cache_{typeName}.");
    }

    [Serializable]
    class QuantityCache
    {
        public List<QuantityEntry> entries = new();
    }

    [Serializable]
    class QuantityEntry
    {
        public string item;
        public int quantity;
    }

    [Serializable]
    class PendingGrant
    {
        public string item;
        public int amount;
    }

    [Serializable]
    class PendingGrantQueue
    {
        public List<PendingGrant> items = new();
    }
}
