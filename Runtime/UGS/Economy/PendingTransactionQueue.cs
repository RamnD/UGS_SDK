using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// Offline transaction queue (positive amount — credit, negative — debit).
/// Accumulates operations while offline and sends them on the next connection.
/// </summary>
internal sealed class PendingTransactionQueue<TCurrency> where TCurrency : struct, Enum
{
    private const string PrefsKey = "economy_pending_adds";

    private readonly ICurrencyMapper<TCurrency> _mapper;

    public PendingTransactionQueue(ICurrencyMapper<TCurrency> mapper) => _mapper = mapper;

    /// <summary>
    /// Enqueues a transaction and saves to disk immediately.
    /// </summary>
    public void Enqueue(TCurrency type, int amount)
    {
        var queue = Load();
        queue.items.Add(new PendingTx { currency = type.ToString(), amount = amount });
        Persist(queue);
    }

    /// <summary>
    /// Sends queued transactions to the server one by one.
    /// On first error — <see cref="InventoryOperationException"/> (Reason = <see cref="InventoryFailureReason.PendingTransactionsFlushFailed"/>).
    /// </summary>
    public async Task FlushAsync(BalanceCache<TCurrency> cache, CancellationToken cancellationToken = default)
    {
        var queue = Load();
        if (queue.items.Count == 0) return;

        var processed = 0;
        foreach (var tx in queue.items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Enum.TryParse<TCurrency>(tx.currency, out var type))
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
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Economy] Pending queue flush error ({tx.currency}): {e.Message}");
                throw new InventoryOperationException(
                    InventoryFailureReason.PendingTransactionsFlushFailed,
                    "Failed to upload pending offline credit transactions.",
                    e);
            }
        }

        queue.items.RemoveRange(0, processed);

        if (queue.items.Count == 0)
            PlayerPrefs.DeleteKey(PrefsKey);
        else
            Persist(queue);

        PlayerPrefs.Save();
    }

    private static PendingQueue Load()
    {
        var json = PlayerPrefs.GetString(PrefsKey, "{}");
        return JsonUtility.FromJson<PendingQueue>(json) ?? new PendingQueue();
    }

    private static void Persist(PendingQueue queue)
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(queue));
        PlayerPrefs.Save();
    }

    [Serializable] private class PendingTx    { public string currency; public int amount; }
    [Serializable] private class PendingQueue { public List<PendingTx> items = new(); }
}
