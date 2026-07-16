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
/// Per-currency amounts are coalesced into a single net delta.
/// </summary>
internal sealed class PendingTransactionQueue<TCurrency> where TCurrency : struct, Enum
{
    const string PrefsKey = "economy_pending_tx";
    const string LegacyPrefsKey = "economy_pending_adds";

    readonly ICurrencyMapper<TCurrency> _mapper;

    public PendingTransactionQueue(ICurrencyMapper<TCurrency> mapper) => _mapper = mapper;

    /// <summary>True when at least one non-zero pending delta remains on disk.</summary>
    public bool HasPending
    {
        get
        {
            var queue = Load();
            return queue.items != null && queue.items.Count > 0;
        }
    }

    /// <summary>
    /// Enqueues a signed delta and saves to disk immediately.
    /// Same-currency entries are coalesced; a zero net removes the entry.
    /// </summary>
    public void Enqueue(TCurrency type, int amount)
    {
        if (amount == 0)
            return;

        var queue = Load();
        string key = type.ToString();

        for (int i = 0; i < queue.items.Count; i++)
        {
            PendingTx existing = queue.items[i];
            if (!string.Equals(existing.currency, key, StringComparison.Ordinal))
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
                queue.items[i] = existing;
                Debug.Log($"[Economy] Queued {key} net → {existing.amount}");
            }

            Persist(queue);
            return;
        }

        queue.items.Add(new PendingTx { currency = key, amount = amount });
        Debug.Log($"[Economy] Queued {(amount >= 0 ? "+" : "")}{amount} {key}");
        Persist(queue);
    }

    /// <summary>
    /// Sends queued net deltas to the server one by one.
    /// On recoverable failure — stops, keeps the remaining tail on disk, returns without throwing.
    /// On non-recoverable failure — throws <see cref="InventoryOperationException"/>.
    /// </summary>
    public async Task FlushAsync(BalanceCache<TCurrency> cache, CancellationToken cancellationToken = default)
    {
        var queue = Load();
        if (queue.items.Count == 0)
            return;

        Debug.Log($"[Economy] Flush started ({queue.items.Count} pending).");
        int processed = 0;

        foreach (PendingTx tx in queue.items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tx.amount == 0 || !Enum.TryParse(tx.currency, out TCurrency type))
            {
                processed++;
                continue;
            }

            try
            {
                var balance = tx.amount >= 0
                    ? await EconomyService.Instance.PlayerBalances
                        .IncrementBalanceAsync(_mapper.ToServiceId(type), tx.amount)
                    : await EconomyService.Instance.PlayerBalances
                        .DecrementBalanceAsync(_mapper.ToServiceId(type), Math.Abs(tx.amount));

                cache.Set(type, balance.Balance);
                processed++;
            }
            catch (OperationCanceledException)
            {
                PersistRemaining(queue, processed);
                throw;
            }
            catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
            {
                Debug.LogWarning(
                    $"[Economy] Flush paused ({tx.currency} {tx.amount}): {e.Message}. " +
                    "Will retry on next RefreshBalancesAsync.");
                PersistRemaining(queue, processed);
                cache.Save();
                return;
            }
            catch (Exception e)
            {
                PersistRemaining(queue, processed);
                cache.Save();
                Debug.LogError($"[Economy] Pending queue flush error ({tx.currency}): {e.Message}");
                throw new InventoryOperationException(
                    InventoryFailureReason.PendingTransactionsFlushFailed,
                    "Failed to upload pending offline transactions.",
                    e);
            }
        }

        PersistRemaining(queue, processed);
        cache.Save();
        Debug.Log("[Economy] Flush completed.");
    }

    static void PersistRemaining(PendingQueue queue, int processed)
    {
        if (processed > 0)
            queue.items.RemoveRange(0, Math.Min(processed, queue.items.Count));

        if (queue.items.Count == 0)
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.DeleteKey(LegacyPrefsKey);
        }
        else
        {
            Persist(queue);
        }

        PlayerPrefs.Save();
    }

    static PendingQueue Load()
    {
        MigrateLegacyKeyIfNeeded();

        string json = PlayerPrefs.GetString(PrefsKey, "{}");
        PendingQueue queue = JsonUtility.FromJson<PendingQueue>(json) ?? new PendingQueue();
        queue.items ??= new List<PendingTx>();
        CoalesceInPlace(queue);
        return queue;
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

    /// <summary>Merges duplicate currency rows (e.g. after legacy migration).</summary>
    static void CoalesceInPlace(PendingQueue queue)
    {
        if (queue.items.Count <= 1)
            return;

        var nets = new Dictionary<string, long>(StringComparer.Ordinal);
        var order = new List<string>();

        for (int i = 0; i < queue.items.Count; i++)
        {
            PendingTx tx = queue.items[i];
            if (string.IsNullOrEmpty(tx.currency) || tx.amount == 0)
                continue;

            if (!nets.ContainsKey(tx.currency))
                order.Add(tx.currency);

            nets[tx.currency] = nets.TryGetValue(tx.currency, out long current)
                ? current + tx.amount
                : tx.amount;
        }

        queue.items.Clear();
        for (int i = 0; i < order.Count; i++)
        {
            string currency = order[i];
            long net = nets[currency];
            if (net == 0 || net > int.MaxValue || net < int.MinValue)
                continue;

            queue.items.Add(new PendingTx { currency = currency, amount = (int)net });
        }
    }

    static void Persist(PendingQueue queue)
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(queue));
        PlayerPrefs.Save();
    }

    [Serializable]
    class PendingTx
    {
        public string currency;
        public int amount;
    }

    [Serializable]
    class PendingQueue
    {
        public List<PendingTx> items = new();
    }
}
