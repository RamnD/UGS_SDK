using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Player balance service. TCurrency is the project currency enum (e.g. CurrencyType).
/// All enum → string ID mapping is delegated to <see cref="ICurrencyMapper{TCurrency}"/>.
/// <para>
/// Offline strategy is defined by the mapper:
/// Add and Spend (where the mapper allows) — optimistic cache + queue; otherwise network is required.
/// </para>
/// <para>
/// Network / server error → <see cref="InventoryOperationException"/>.
/// For spend: insufficient funds (after server reconciliation) → <see cref="TrySpendCurrencyAsync"/> returns false, not an exception.
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
    /// Syncs balances with the server. Call at game start and after reconnect.
    /// If offline — loads the last known cache from PlayerPrefs.
    /// </summary>
    Task RefreshBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Credits currency (e.g. level reward, ad watch).
    /// Offline behavior is defined by <see cref="ICurrencyMapper{TCurrency}.IsOfflineAllowed"/>.
    /// </summary>
    Task AddCurrencyAsync(TCurrency type, int amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Debits currency. Offline — if allowed by the mapper (optimistic cache + queue).
    /// Returns false if insufficient funds. On server desync (HTTP 422) updates the cache.
    /// </summary>
    /// <returns>True if the server confirmed the transaction.</returns>
    Task<bool> TrySpendCurrencyAsync(TCurrency type, int amount, CancellationToken cancellationToken = default);
}
