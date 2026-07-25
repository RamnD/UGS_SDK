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
    readonly object _refreshGate = new object();
    readonly object _mutationGate = new object();
    readonly HashSet<TItem> _mutatingItems = new();
    Task _refreshTask;

    const int StatusPending = 0;
    const int StatusInFlight = 1;

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
    public void ClearLocalCache()
    {
        _quantities.Clear();
        lock (_pendingSync)
        {
            PlayerPrefs.DeleteKey(_pendingPrefsKey);
        }

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
            await FlushPendingGrantsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (HasPendingGrants())
            {
                Debug.LogWarning(
                    "[Consumables] Pending grants not fully flushed — keeping local quantities until next refresh.");
                SaveToPrefs();
                return;
            }

            var result = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerBalances.GetBalancesAsync(),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            RebuildFromBalances(result.Balances);
            ApplyPendingOnTop();
            SaveToPrefs();
            NetworkStatus.ReportSuccess();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InventoryOperationException)
        {
            throw;
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e)
                                  || EconomyErrorClassifier.IsIndeterminate(e))
        {
            NetworkStatus.ReportFailure();
            Debug.LogWarning($"[Consumables] Balance sync failed (transport) — using cache: {e.Message}");
            LoadFromPrefs();
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

        if (!TryBeginMutation(id))
        {
            Debug.LogWarning($"[Consumables] Consume rejected for '{id}' — mutation already in flight.");
            return false;
        }

        try
        {
            return await TryConsumeCoreAsync(id, amount, cancellationToken);
        }
        finally
        {
            EndMutation(id);
        }
    }

    async Task<bool> TryConsumeCoreAsync(TItem id, int amount, CancellationToken cancellationToken)
    {
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
            var result = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerBalances.DecrementBalanceAsync(
                    _mapper.ToServiceId(id),
                    amount),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            SetQuantity(id, ToIntQuantity(result.Balance));
            SaveToPrefs();
            RaiseChanged(id);
            NetworkStatus.ReportSuccess();
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
        catch (Exception e) when (EconomyErrorClassifier.IsIndeterminate(e))
        {
            NetworkStatus.ReportFailure();
            Debug.LogWarning($"[Consumables] Consume indeterminate for {id} — refreshing: {e.Message}");
            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (Exception refreshEx)
            {
                Debug.LogWarning($"[Consumables] Reconcile after indeterminate consume: {refreshEx.Message}");
            }

            // Do not local-debit — server may already have consumed.
            return false;
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
        {
            NetworkStatus.ReportFailure();
            Debug.LogWarning($"[Consumables] Consume failed (recoverable) for {id}: {e.Message}");
            return false;
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

        if (!TryBeginMutation(id))
        {
            Debug.LogWarning($"[Consumables] Grant rejected for '{id}' — mutation already in flight.");
            return false;
        }

        try
        {
            return await TryGrantCoreAsync(id, amount, cancellationToken);
        }
        finally
        {
            EndMutation(id);
        }
    }

    async Task<bool> TryGrantCoreAsync(TItem id, int amount, CancellationToken cancellationToken)
    {
        if (!NetworkStatus.IsOnline)
        {
            if (!_mapper.IsOfflineAllowed(id, InventoryOperation.Add))
                return false;

            ApplyLocalGrant(id, amount);
            return true;
        }

        long before = GetQuantity(id);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerBalances.IncrementBalanceAsync(
                    _mapper.ToServiceId(id),
                    amount),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            SetQuantity(id, ToIntQuantity(result.Balance));
            SaveToPrefs();
            RaiseChanged(id);
            NetworkStatus.ReportSuccess();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (EconomyErrorClassifier.IsIndeterminate(e)
                                  && _mapper.IsOfflineAllowed(id, InventoryOperation.Add))
        {
            NetworkStatus.ReportFailure();
            Debug.LogWarning(
                $"[Consumables] Grant {id} indeterminate — reconciling: {e.Message}");
            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch (Exception refreshEx)
            {
                Debug.LogWarning($"[Consumables] Reconcile refresh failed: {refreshEx.Message}");
            }

            if (GetQuantity(id) >= before + amount)
            {
                Debug.Log($"[Consumables] Indeterminate grant {id} +{amount} already on server.");
                return true;
            }

            ApplyLocalGrant(id, amount);
            return true;
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e)
                                  && _mapper.IsOfflineAllowed(id, InventoryOperation.Add))
        {
            NetworkStatus.ReportFailure();
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

    bool TryBeginMutation(TItem id)
    {
        lock (_mutationGate)
            return _mutatingItems.Add(id);
    }

    void EndMutation(TItem id)
    {
        lock (_mutationGate)
            _mutatingItems.Remove(id);
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
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingGrant g = queue.items[i];
                if (g.status == StatusInFlight)
                {
                    g.status = StatusPending;
                    queue.items[i] = g;
                }
            }

            EnsurePendingIds(queue);
            PersistPendingUnlocked(queue);
            if (queue.items.Count == 0)
                return;
            work = new List<PendingGrant>(queue.items);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Consumables] Flushing {work.Count} pending grant(s).");
#endif

        foreach (PendingGrant snapshot in work)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (snapshot.amount <= 0
                || string.IsNullOrEmpty(snapshot.id)
                || !Enum.TryParse(snapshot.item, out TItem id)
                || !_mapper.IsConsumable(id))
            {
                RemovePendingById(snapshot.id);
                continue;
            }

            if (!TryMarkGrantInFlight(snapshot.id, out int amount))
                continue;

            try
            {
                var result = await NetworkRequest.WithTimeout(
                    EconomyService.Instance.PlayerBalances.IncrementBalanceAsync(
                        _mapper.ToServiceId(id),
                        amount),
                    cancellationToken);
                SetQuantity(id, ToIntQuantity(result.Balance));
                RemovePendingById(snapshot.id);
                RaiseChanged(id);
                NetworkStatus.ReportSuccess();
            }
            catch (OperationCanceledException)
            {
                RevertGrantToPending(snapshot.id);
                throw;
            }
            catch (Exception e) when (EconomyErrorClassifier.IsIndeterminate(e))
            {
                NetworkStatus.ReportFailure();
                Debug.LogWarning(
                    $"[Consumables] Pending grant flush indeterminate ({snapshot.item} +{amount}): {e.Message}");
                // Leave as pending (not in-flight) — next refresh reconciles via GetBalances + ApplyPendingOnTop.
                RevertGrantToPending(snapshot.id);
                return;
            }
            catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
            {
                NetworkStatus.ReportFailure();
                Debug.LogWarning(
                    $"[Consumables] Pending grant flush paused ({snapshot.item} +{amount}): {e.Message}");
                RevertGrantToPending(snapshot.id);
                return;
            }
            catch (Exception e)
            {
                RevertGrantToPending(snapshot.id);
                Debug.LogError($"[Consumables] Pending grant flush failed ({snapshot.item}): {e.Message}");
                throw new InventoryOperationException(
                    InventoryFailureReason.PendingTransactionsFlushFailed,
                    "Failed to upload pending consumable grants.",
                    e);
            }
        }
    }

    bool HasPendingGrants()
    {
        lock (_pendingSync)
        {
            var queue = LoadPendingUnlocked();
            return queue.items != null && queue.items.Count > 0;
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
            EnsurePendingIds(queue);
            string key = id.ToString();
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingGrant existing = queue.items[i];
                if (!string.Equals(existing.item, key, StringComparison.Ordinal))
                    continue;
                if (existing.status == StatusInFlight)
                    continue;

                long net = (long)existing.amount + amount;
                if (net > int.MaxValue)
                {
                    Debug.LogError($"[Consumables] Pending grant overflow for {key}.");
                    return;
                }

                existing.amount = (int)net;
                existing.status = StatusPending;
                queue.items[i] = existing;
                PersistPendingUnlocked(queue);
                return;
            }

            queue.items.Add(new PendingGrant
            {
                id = Guid.NewGuid().ToString("N"),
                item = key,
                amount = amount,
                status = StatusPending,
            });
            PersistPendingUnlocked(queue);
        }
    }

    bool TryMarkGrantInFlight(string id, out int amount)
    {
        amount = 0;
        if (string.IsNullOrEmpty(id))
            return false;

        lock (_pendingSync)
        {
            var queue = LoadPendingUnlocked();
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingGrant g = queue.items[i];
                if (!string.Equals(g.id, id, StringComparison.Ordinal))
                    continue;

                amount = g.amount;
                g.status = StatusInFlight;
                queue.items[i] = g;
                PersistPendingUnlocked(queue);
                return true;
            }
        }

        return false;
    }

    void RemovePendingById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        lock (_pendingSync)
        {
            var queue = LoadPendingUnlocked();
            for (int i = queue.items.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(queue.items[i].id, id, StringComparison.Ordinal))
                    continue;
                queue.items.RemoveAt(i);
                PersistPendingUnlocked(queue);
                return;
            }
        }
    }

    void RevertGrantToPending(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        lock (_pendingSync)
        {
            var queue = LoadPendingUnlocked();
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingGrant g = queue.items[i];
                if (!string.Equals(g.id, id, StringComparison.Ordinal))
                    continue;
                g.status = StatusPending;
                queue.items[i] = g;
                PersistPendingUnlocked(queue);
                return;
            }
        }
    }

    static void EnsurePendingIds(PendingGrantQueue queue)
    {
        for (int i = 0; i < queue.items.Count; i++)
        {
            PendingGrant g = queue.items[i];
            if (string.IsNullOrEmpty(g.id))
            {
                g.id = Guid.NewGuid().ToString("N");
                queue.items[i] = g;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Consumables] Migrated cache key → consumables_currency_cache_{typeName}.");
#endif
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
        public string id;
        public string item;
        public int amount;
        /// <summary>0 = pending, 1 = in_flight.</summary>
        public int status;
    }

    [Serializable]
    class PendingGrantQueue
    {
        public List<PendingGrant> items = new();
    }
}
