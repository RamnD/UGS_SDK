using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Player balance service. TCurrency is the project currency enum (e.g. CurrencyType).
/// All enum → string ID mapping is delegated to <see cref="ICurrencyMapper{TCurrency}"/>.
/// <para>
/// Default write path is <b>deferred</b>: when the mapper allows the operation offline,
/// Add/Spend apply to the local cache immediately and enqueue a durable pending delta.
/// Call <see cref="FlushPendingAsync"/> or <see cref="RefreshBalancesAsync"/> at game sync
/// anchors (leave shop, level load, victory/defeat, inventory exit). Pass
/// <c>syncImmediately: true</c> to force an online UGS write for a specific transaction.
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
    /// True when durable pending / in-flight / unconfirmed deltas remain.
    /// </summary>
    bool HasPendingTransactions { get; }

    /// <summary>
    /// Outcome of the last <see cref="RefreshBalancesAsync"/>. Default
    /// <see cref="EconomyRefreshResult.None"/> before the first refresh.
    /// </summary>
    EconomyRefreshResult LastRefreshResult { get; }

    /// <summary>
    /// Syncs balances with the server. Call at game start, after reconnect, and on resume.
    /// Flushes the pending queue first. If offline or refresh is recoverable-failed —
    /// loads / keeps the last known cache from PlayerPrefs.
    /// While pending deltas remain, does not overwrite local cache with server balances.
    /// </summary>
    Task RefreshBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads pending deltas to UGS without pulling server balances.
    /// No-op when offline or the queue is empty. Prefer this (or
    /// <see cref="RefreshBalancesAsync"/>) at gameplay sync anchors.
    /// </summary>
    Task FlushPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Credits currency (e.g. level reward, shop grant, ad watch).
    /// Default: optimistic local cache + pending queue when the mapper allows Add offline.
    /// Pass <paramref name="syncImmediately"/> to write to UGS right away when online
    /// (falls back to the queue if offline and the mapper allows it).
    /// </summary>
    Task AddCurrencyAsync(
        TCurrency type,
        int amount,
        CancellationToken cancellationToken = default,
        bool syncImmediately = false);

    /// <summary>
    /// Debits currency. Default: optimistic local cache + pending queue when the mapper
    /// allows Spend offline. Pass <paramref name="syncImmediately"/> for an online write
    /// when the device is online. Returns false if insufficient funds, offline spend
    /// disallowed, or a non-queued recoverable failure. On server 422 refreshes the cache.
    /// </summary>
    /// <returns>True if applied (server-confirmed or queued locally).</returns>
    Task<bool> TrySpendCurrencyAsync(
        TCurrency type,
        int amount,
        CancellationToken cancellationToken = default,
        bool syncImmediately = false);

    /// <summary>
    /// Clears in-memory balances and durable pending queue (PlayerPrefs).
    /// Call on account delete / switch before a new player session uses this service.
    /// </summary>
    void ClearLocalCache();
}
