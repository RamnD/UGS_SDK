using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Player balance service. TCurrency is the project currency enum (e.g. CurrencyType).
/// All enum → string ID mapping is delegated to <see cref="ICurrencyMapper{TCurrency}"/>.
/// <para>
/// Offline / recoverable-network strategy is defined by the mapper:
/// Add and Spend (where the mapper allows) — optimistic PlayerPrefs cache + durable pending queue;
/// otherwise network is required. The queue flushes on the next successful
/// <see cref="RefreshBalancesAsync"/>.
/// </para>
/// <para>
/// Hard provider / config errors on Add → <see cref="InventoryOperationException"/>.
/// Spend never throws for network or insufficient funds: returns false instead.
/// </para>
/// </summary>
/// <typeparam name="TCurrency">Project enum of currency types.</typeparam>
public interface IInventoryService<TCurrency> where TCurrency : struct, Enum
{
    /// <summary>
    /// Returns the cached balance synchronously. Safe to call from Update/UI.
    /// </summary>
    long GetCachedBalance(TCurrency type);

    /// <summary>
    /// Syncs balances with the server. Call at game start, after reconnect, and on resume.
    /// Flushes the pending queue first. If offline or refresh is recoverable-failed —
    /// loads / keeps the last known cache from PlayerPrefs.
    /// While pending deltas remain, does not overwrite local cache with server balances.
    /// </summary>
    Task RefreshBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Credits currency (e.g. level reward, ad watch).
    /// Offline or recoverable network failure — if allowed by the mapper, applies locally
    /// and enqueues for the next <see cref="RefreshBalancesAsync"/>.
    /// </summary>
    Task AddCurrencyAsync(TCurrency type, int amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debits currency. Offline / recoverable network — if allowed by the mapper
    /// (optimistic cache + queue). Returns false if insufficient funds, offline spend
    /// disallowed, or a non-queued recoverable failure. On server 422 refreshes the cache.
    /// </summary>
    /// <returns>True if applied (server-confirmed or queued locally).</returns>
    Task<bool> TrySpendCurrencyAsync(TCurrency type, int amount, CancellationToken cancellationToken = default);
}
