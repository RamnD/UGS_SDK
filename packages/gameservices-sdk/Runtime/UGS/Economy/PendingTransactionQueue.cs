using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// Durable transaction queue (positive amount — credit, negative — debit).
/// Status flow: <c>pending → in_flight → (removed | unconfirmed | pending)</c>.
/// <c>unconfirmed</c> = timed-out write that may have landed on the server; resolved after
/// the next successful balance fetch via <see cref="ResolveUnconfirmed"/>.
/// </summary>
internal sealed class PendingTransactionQueue<TCurrency> where TCurrency : struct, Enum
{
    const string PrefsKey = "economy_pending_tx";
    const string LegacyPrefsKey = "economy_pending_adds";

    const int StatusPending = 0;
    const int StatusInFlight = 1;
    const int StatusUnconfirmed = 2;

    readonly ICurrencyMapper<TCurrency> _mapper;
    readonly object _sync = new object();
    readonly object _flushGate = new object();
    Task _flushTask;

    public PendingTransactionQueue(ICurrencyMapper<TCurrency> mapper) => _mapper = mapper;

    /// <summary>True when any durable row remains (pending / in-flight / unconfirmed).</summary>
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

    /// <summary>True when a flushable <see cref="StatusPending"/> row exists.</summary>
    public bool HasFlushablePending
    {
        get
        {
            lock (_sync)
            {
                var queue = LoadUnlocked();
                if (queue.items == null)
                    return false;
                for (int i = 0; i < queue.items.Count; i++)
                {
                    if (queue.items[i].status == StatusPending && queue.items[i].amount != 0)
                        return true;
                }

                return false;
            }
        }
    }

    /// <summary>True when an unconfirmed (timed-out) row must be reconciled after GetBalances.</summary>
    public bool HasUnconfirmed
    {
        get
        {
            lock (_sync)
            {
                var queue = LoadUnlocked();
                if (queue.items == null)
                    return false;
                for (int i = 0; i < queue.items.Count; i++)
                {
                    if (queue.items[i].status == StatusUnconfirmed)
                        return true;
                }

                return false;
            }
        }
    }

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
                if (existing.status == StatusInFlight || existing.status == StatusUnconfirmed)
                    continue;

                long net = (long)existing.amount + amount;
                if (net > int.MaxValue || net < int.MinValue)
                {
                    AppLog.Error("Economy", $"Pending queue overflow for {key} ({existing.amount} + {amount}). " +
                        "Keeping previous value.");
                    return;
                }

                if (net == 0)
                {
                    queue.items.RemoveAt(i);
                }
                else
                {
                    existing.amount = (int)net;
                    existing.status = StatusPending;
                    if (string.IsNullOrEmpty(existing.id))
                        existing.id = Guid.NewGuid().ToString("N");
                    queue.items[i] = existing;
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
                balanceBefore = 0,
            });
            PersistUnlocked(queue);
        }
    }

    /// <summary>
    /// Re-applies pending (not in-flight / unconfirmed) deltas on top of a server snapshot
    /// so UI stays optimistic until flush completes.
    /// </summary>
    public void ApplyPendingOnTop(BalanceCache<TCurrency> cache)
    {
        if (cache == null)
            return;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingTx tx = queue.items[i];
                if (tx.status != StatusPending || tx.amount == 0)
                    continue;
                if (!Enum.TryParse(tx.currency, out TCurrency type))
                    continue;

                cache.Set(type, cache.Get(type) + tx.amount);
            }
        }
    }

    /// <summary>
    /// After a successful GetBalances: drop unconfirmed rows that already landed on the server;
    /// otherwise promote them back to pending for a safe retry.
    /// </summary>
    public void ResolveUnconfirmed(BalanceCache<TCurrency> cache)
    {
        if (cache == null)
            return;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            bool changed = false;
            for (int i = queue.items.Count - 1; i >= 0; i--)
            {
                PendingTx tx = queue.items[i];
                if (tx.status != StatusUnconfirmed)
                    continue;
                if (!Enum.TryParse(tx.currency, out TCurrency type))
                {
                    queue.items.RemoveAt(i);
                    changed = true;
                    continue;
                }

                long server = cache.Get(type);
                bool landed = tx.amount >= 0
                    ? server >= tx.balanceBefore + tx.amount
                    : server <= tx.balanceBefore + tx.amount;

                if (landed)
                {
                    AppLog.Info("Economy", $"Unconfirmed {tx.currency} {tx.amount} already on server — dropping.");
                    queue.items.RemoveAt(i);
                }
                else
                {
                    AppLog.Warn("Economy", $"Unconfirmed {tx.currency} {tx.amount} missing on server — re-queue pending.");
                    tx.status = StatusPending;
                    queue.items[i] = tx;
                }

                changed = true;
            }

            if (changed)
                PersistUnlocked(queue);
        }
    }

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
            // Crash recovery: abandoned in_flight → unconfirmed (may have landed).
            for (int i = 0; i < queue.items.Count; i++)
            {
                PendingTx tx = queue.items[i];
                if (tx.status == StatusInFlight)
                {
                    tx.status = StatusUnconfirmed;
                    queue.items[i] = tx;
                }
            }

            PersistUnlocked(queue);
            work = new List<PendingTx>();
            for (int i = 0; i < queue.items.Count; i++)
            {
                if (queue.items[i].status == StatusPending)
                    work.Add(queue.items[i]);
            }
        }

        if (work.Count == 0)
            return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        AppLog.Info("Economy", $"Flush started ({work.Count} pending).");
#endif

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

            long balanceBefore = cache.Get(type);
            if (!TryMarkInFlight(snapshot.id, balanceBefore, out int amount))
                continue;

            try
            {
                var balance = amount >= 0
                    ? await NetworkRequest.WithTimeout(
                        EconomyService.Instance.PlayerBalances
                            .IncrementBalanceAsync(_mapper.ToServiceId(type), amount),
                        cancellationToken)
                    : await NetworkRequest.WithTimeout(
                        EconomyService.Instance.PlayerBalances
                            .DecrementBalanceAsync(_mapper.ToServiceId(type), Math.Abs(amount)),
                        cancellationToken);

                cache.Set(type, balance.Balance);
                RemoveById(snapshot.id);
                NetworkStatus.ReportSuccess();
            }
            catch (OperationCanceledException)
            {
                MarkUnconfirmed(snapshot.id, balanceBefore);
                cache.Save();
                throw;
            }
            catch (EconomyException e) when (
                amount < 0
                && e.Reason == EconomyExceptionReason.UnprocessableTransaction)
            {
                // Impossible offline spend (server balance too low) — drop the dead row and continue.
                // Credits that 422 are NOT dropped here (would lose a legitimate grant).
                AppLog.Warn("Economy", $"Dropping impossible pending spend ({snapshot.currency} {amount}): {e.Message}. " +
                    "Will reconcile from GetBalances.");
                RemoveById(snapshot.id);
                continue;
            }
            catch (Exception e) when (EconomyErrorClassifier.IsIndeterminate(e))
            {
                NetworkStatus.ReportFailure();
                AppLog.Warn("Economy", $"Flush indeterminate ({snapshot.currency} {amount}): {e.Message}. " +
                    "Will reconcile after next GetBalances.");
                MarkUnconfirmed(snapshot.id, balanceBefore);
                cache.Save();
                return;
            }
            catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
            {
                NetworkStatus.ReportFailure();
                AppLog.Warn("Economy", $"Flush paused ({snapshot.currency} {amount}): {e.Message}. " +
                    "Will retry on next RefreshBalancesAsync.");
                RevertToPending(snapshot.id);
                cache.Save();
                return;
            }
            catch (Exception e)
            {
                RevertToPending(snapshot.id);
                cache.Save();
                AppLog.Error("Economy", $"Pending queue flush error ({snapshot.currency}): {e.Message}");
                throw new InventoryOperationException(
                    InventoryFailureReason.PendingTransactionsFlushFailed,
                    "Failed to upload pending offline transactions.",
                    e);
            }
        }

        cache.Save();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        AppLog.Info("Economy", "Flush completed.");
#endif
    }

    bool TryMarkInFlight(string id, long balanceBefore, out int amount)
    {
        amount = 0;
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
                if (tx.status != StatusPending)
                    return false;

                amount = tx.amount;
                tx.status = StatusInFlight;
                tx.balanceBefore = balanceBefore;
                queue.items[i] = tx;
                PersistUnlocked(queue);
                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        lock (_sync)
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.DeleteKey(LegacyPrefsKey);
            PlayerPrefs.Save();
        }
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

    void MarkUnconfirmed(string id, long balanceBefore)
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

                tx.status = StatusUnconfirmed;
                tx.balanceBefore = balanceBefore;
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
    }

    static void CoalescePendingInPlace(PendingQueue queue)
    {
        if (queue.items.Count <= 1)
            return;

        var pendingNets = new Dictionary<string, long>(StringComparer.Ordinal);
        var pendingIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var pendingOrder = new List<string>();
        var reserved = new List<PendingTx>();

        for (int i = 0; i < queue.items.Count; i++)
        {
            PendingTx tx = queue.items[i];
            if (string.IsNullOrEmpty(tx.currency) || tx.amount == 0)
                continue;

            if (tx.status == StatusInFlight || tx.status == StatusUnconfirmed)
            {
                reserved.Add(tx);
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
        for (int i = 0; i < reserved.Count; i++)
            queue.items.Add(reserved[i]);

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
        /// <summary>0 = pending, 1 = in_flight, 2 = unconfirmed.</summary>
        public int status;
        /// <summary>Cached balance before the in-flight / unconfirmed write.</summary>
        public long balanceBefore;
    }

    [Serializable]
    class PendingQueue
    {
        public List<PendingTx> items = new();
    }
}
