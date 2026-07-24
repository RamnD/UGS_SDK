using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// Durable transaction queue (positive amount — credit, negative — debit).
/// Accumulates operations while offline / on recoverable network failure and
/// flushes them on the next successful <see cref="FlushAsync"/>.
/// Per-currency pending amounts are coalesced; each logical row has a stable id
/// and status <c>pending → in_flight</c> (removed on success).
/// Enqueue and flush serialize on <see cref="_sync"/> and always re-read before persist
/// so mid-flush enqueues are not overwritten.
/// </summary>
internal sealed class PendingTransactionQueue<TCurrency> where TCurrency : struct, Enum
{
    const string PrefsKey = "economy_pending_tx";
    const string LegacyPrefsKey = "economy_pending_adds";

    const int StatusPending = 0;
    const int StatusInFlight = 1;

    readonly ICurrencyMapper<TCurrency> _mapper;
    readonly object _sync = new object();
    readonly object _flushGate = new object();
    Task _flushTask;

    public PendingTransactionQueue(ICurrencyMapper<TCurrency> mapper) => _mapper = mapper;

    /// <summary>True when at least one non-zero pending/in-flight delta remains on disk.</summary>
    public bool HasPending
    {
        get
        {
            lock (_sync)
            {
                var queue = LoadUnlocked();
                return queue.items != null && queue.items.Count > 0;
            }
        }
    }

    /// <summary>
    /// Enqueues a signed delta and saves to disk immediately.
    /// Same-currency <see cref="StatusPending"/> entries are coalesced; in-flight rows are left alone
    /// and a separate pending row is created/merged for the new delta.
    /// </summary>
    public void Enqueue(TCurrency type, int amount)
    {
        if (amount == 0)
            return;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            string key = type.ToString();

            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingTx existing = queue.items[i];
                if (!string.Equals(existing.currency, key, StringComparison.Ordinal))
                    continue;
                if (existing.status == StatusInFlight)
                    continue;

                long net = (long)existing.amount + amount;
                if (net > int.MaxValue || net < int.MinValue)
                {
                    Debug.LogError(
                        $"[Economy] Pending queue overflow for {key} ({existing.amount} + {amount}). " +
                        "Keeping previous value.");
                    return;
                }

                if (net == 0)
                {
                    queue.items.RemoveAt(i);
                    Debug.Log($"[Economy] Queued net 0 {key} — removed pending entry.");
                }
                else
                {
                    existing.amount = (int)net;
                    existing.status = StatusPending;
                    if (string.IsNullOrEmpty(existing.id))
                        existing.id = Guid.NewGuid().ToString("N");
                    queue.items[i] = existing;
                    Debug.Log($"[Economy] Queued {key} net → {existing.amount}");
                }

                PersistUnlocked(queue);
                return;
            }

            queue.items.Add(new PendingTx
            {
                id = Guid.NewGuid().ToString("N"),
                currency = key,
                amount = amount,
                status = StatusPending,
            });
            Debug.Log($"[Economy] Queued {(amount >= 0 ? "+" : "")}{amount} {key}");
            PersistUnlocked(queue);
        }
    }

    /// <summary>
    /// Single-flight flush: concurrent callers await the in-flight flush.
    /// On recoverable failure — stops, keeps the remaining tail on disk, returns without throwing.
    /// On non-recoverable failure — throws <see cref="InventoryOperationException"/>.
    /// </summary>
    public async Task FlushAsync(BalanceCache<TCurrency> cache, CancellationToken cancellationToken = default)
    {
        Task flush;
        lock (_flushGate)
        {
            if (_flushTask != null && !_flushTask.IsCompleted)
                flush = _flushTask;
            else
            {
                flush = FlushCoreAsync(cache, cancellationToken);
                _flushTask = flush;
            }
        }

        try
        {
            await flush;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_flushGate)
            {
                if (ReferenceEquals(_flushTask, flush) && flush.IsCompleted)
                    _flushTask = null;
            }
        }
    }

    async Task FlushCoreAsync(BalanceCache<TCurrency> cache, CancellationToken cancellationToken)
    {
        List<PendingTx> work;
        lock (_sync)
        {
            var queue = LoadUnlocked();
            // Crash recovery: in_flight from a previous session is retried as pending.
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingTx tx = queue.items[i];
                if (tx.status == StatusInFlight)
                {
                    tx.status = StatusPending;
                    queue.items[i] = tx;
                }
            }

            PersistUnlocked(queue);
            work = new List<PendingTx>(queue.items);
        }

        if (work.Count == 0)
            return;

        Debug.Log($"[Economy] Flush started ({work.Count} pending).");

        foreach (PendingTx snapshot in work)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (snapshot.amount == 0
                || string.IsNullOrEmpty(snapshot.id)
                || !Enum.TryParse(snapshot.currency, out TCurrency type))
            {
                RemoveById(snapshot.id);
                continue;
            }

            if (!TryMarkInFlight(snapshot.id))
                continue; // removed / coalesced away while waiting

            try
            {
                var balance = snapshot.amount >= 0
                    ? await EconomyService.Instance.PlayerBalances
                        .IncrementBalanceAsync(_mapper.ToServiceId(type), snapshot.amount)
                    : await EconomyService.Instance.PlayerBalances
                        .DecrementBalanceAsync(_mapper.ToServiceId(type), Math.Abs(snapshot.amount));

                cache.Set(type, balance.Balance);
                RemoveById(snapshot.id);
            }
            catch (OperationCanceledException)
            {
                RevertToPending(snapshot.id);
                throw;
            }
            catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
            {
                Debug.LogWarning(
                    $"[Economy] Flush paused ({snapshot.currency} {snapshot.amount}): {e.Message}. " +
                    "Will retry on next RefreshBalancesAsync.");
                RevertToPending(snapshot.id);
                cache.Save();
                return;
            }
            catch (Exception e)
            {
                RevertToPending(snapshot.id);
                cache.Save();
                Debug.LogError($"[Economy] Pending queue flush error ({snapshot.currency}): {e.Message}");
                throw new InventoryOperationException(
                    InventoryFailureReason.PendingTransactionsFlushFailed,
                    "Failed to upload pending offline transactions.",
                    e);
            }
        }

        cache.Save();
        Debug.Log("[Economy] Flush completed.");
    }

    bool TryMarkInFlight(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingTx tx = queue.items[i];
                if (!string.Equals(tx.id, id, StringComparison.Ordinal))
                    continue;

                tx.status = StatusInFlight;
                queue.items[i] = tx;
                PersistUnlocked(queue);
                return true;
            }
        }

        return false;
    }

    void RemoveById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            for (int i = queue.items.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(queue.items[i].id, id, StringComparison.Ordinal))
                    continue;

                queue.items.RemoveAt(i);
                PersistUnlocked(queue);
                return;
            }
        }
    }

    void RevertToPending(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingTx tx = queue.items[i];
                if (!string.Equals(tx.id, id, StringComparison.Ordinal))
                    continue;

                tx.status = StatusPending;
                queue.items[i] = tx;
                PersistUnlocked(queue);
                return;
            }
        }
    }

    PendingQueue LoadUnlocked()
    {
        MigrateLegacyKeyIfNeeded();

        string json = PlayerPrefs.GetString(PrefsKey, "{}");
        PendingQueue queue = JsonUtility.FromJson<PendingQueue>(json) ?? new PendingQueue();
        queue.items ??= new List<PendingTx>();
        EnsureIds(queue);
        CoalescePendingInPlace(queue);
        return queue;
    }

    static void EnsureIds(PendingQueue queue)
    {
        for (int i = 0; i < queue.items.Count; i++)
        {
            PendingTx tx = queue.items[i];
            if (string.IsNullOrEmpty(tx.id))
            {
                tx.id = Guid.NewGuid().ToString("N");
                queue.items[i] = tx;
            }
        }
    }

    static void MigrateLegacyKeyIfNeeded()
    {
        if (PlayerPrefs.HasKey(PrefsKey) || !PlayerPrefs.HasKey(LegacyPrefsKey))
            return;

        string legacyJson = PlayerPrefs.GetString(LegacyPrefsKey, "{}");
        PlayerPrefs.SetString(PrefsKey, legacyJson);
        PlayerPrefs.DeleteKey(LegacyPrefsKey);
        PlayerPrefs.Save();
        Debug.Log("[Economy] Migrated pending queue key economy_pending_adds → economy_pending_tx.");
    }

    /// <summary>Merges duplicate pending (not in-flight) currency rows.</summary>
    static void CoalescePendingInPlace(PendingQueue queue)
    {
        if (queue.items.Count <= 1)
            return;

        var pendingNets = new Dictionary<string, long>(StringComparer.Ordinal);
        var pendingIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var pendingOrder = new List<string>();
        var inFlight = new List<PendingTx>();

        for (int i = 0; i < queue.items.Count; i++)
        {
            PendingTx tx = queue.items[i];
            if (string.IsNullOrEmpty(tx.currency) || tx.amount == 0)
                continue;

            if (tx.status == StatusInFlight)
            {
                inFlight.Add(tx);
                continue;
            }

            if (!pendingNets.ContainsKey(tx.currency))
            {
                pendingOrder.Add(tx.currency);
                pendingIds[tx.currency] = string.IsNullOrEmpty(tx.id)
                    ? Guid.NewGuid().ToString("N")
                    : tx.id;
            }

            pendingNets[tx.currency] = pendingNets.TryGetValue(tx.currency, out long current)
                ? current + tx.amount
                : tx.amount;
        }

        queue.items.Clear();
        for (int i = 0; i < inFlight.Count; i++)
            queue.items.Add(inFlight[i]);

        for (int i = 0; i < pendingOrder.Count; i++)
        {
            string currency = pendingOrder[i];
            long net = pendingNets[currency];
            if (net == 0 || net > int.MaxValue || net < int.MinValue)
                continue;

            queue.items.Add(new PendingTx
            {
                id = pendingIds[currency],
                currency = currency,
                amount = (int)net,
                status = StatusPending,
            });
        }
    }

    static void PersistUnlocked(PendingQueue queue)
    {
        if (queue.items == null || queue.items.Count == 0)
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.DeleteKey(LegacyPrefsKey);
        }
        else
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(queue));
        }

        PlayerPrefs.Save();
    }

    [Serializable]
    class PendingTx
    {
        public string id;
        public string currency;
        public int amount;
        /// <summary>0 = pending, 1 = in_flight.</summary>
        public int status;
    }

    [Serializable]
    class PendingQueue
    {
        public List<PendingTx> items = new();
    }
}
