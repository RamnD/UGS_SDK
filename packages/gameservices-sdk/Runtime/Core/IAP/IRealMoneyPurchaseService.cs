using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Portable service for store-backed real-money purchases.
/// Concrete implementations may use Unity IAP, a custom backend, or another store bridge.
/// </summary>
public interface IRealMoneyPurchaseService
{
    /// <summary>True after the store connection and product registration are complete.</summary>
    bool IsInitialized { get; }

    /// <summary>
    /// True after at least one successful store product fetch completed.
    /// Product metadata may still be incomplete if the store omitted some SKUs.
    /// </summary>
    bool AreProductsReady { get; }

    /// <summary>
    /// Initializes the purchase service and registers all product definitions with the store.
    /// Drains any pending (unconfirmed) store transactions after connect.
    /// Safe to call multiple times.
    /// </summary>
    /// <param name="products">Catalog of products to register with Unity IAP / the store.</param>
    /// <param name="cancellationToken">Cancels initialization.</param>
    Task InitializeAsync(
        RealMoneyProductDefinition[] products,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-requests store product metadata when <see cref="AreProductsReady"/> is false.
    /// No-op when already ready or not initialized. Safe after a previous fetch failure.
    /// </summary>
    void EnsureProductsFetched();

    /// <summary>
    /// Fetches existing store purchases and redeems / confirms any <b>pending</b> orders.
    /// Does <b>not</b> grant entitlements from confirmed historical purchases —
    /// that happens only via <see cref="RestorePurchases"/> / <see cref="RestorePurchasesAsync"/>.
    /// Call on resume or after auth if init already ran. Safe to call concurrently
    /// (callers await the same in-flight drain).
    /// </summary>
    Task ProcessPendingPurchasesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a purchase flow for a configured product.
    /// Drains pending store transactions first so stuck receipts do not mix with the new buy.
    /// Returns true when the purchase has been processed successfully.
    /// Rejects with false if another purchase is already in flight (single-flight).
    /// </summary>
    /// <param name="productId"><see cref="RealMoneyProductDefinition.ProductId"/> (Economy / game key).</param>
    /// <param name="cancellationToken">Cancels waiting for the store callback.</param>
    /// <returns>True on success; false on cancel, failure, indeterminate, or busy reject.</returns>
    Task<bool> PurchaseAsync(
        string productId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Outcome of the most recent <see cref="PurchaseAsync"/>.
    /// Valid immediately after that call returns.
    /// </summary>
    RealMoneyPurchaseOutcome LastPurchaseOutcome { get; }

    /// <summary>
    /// True when the most recent <see cref="PurchaseAsync"/> returned false because the
    /// player cancelled the store sheet. Equivalent to
    /// <see cref="LastPurchaseOutcome"/> == <see cref="RealMoneyPurchaseOutcome.Cancelled"/>.
    /// </summary>
    bool LastPurchaseWasUserCancelled { get; }

    /// <summary>
    /// True when rewards were granted (Economy redeem and/or entitlements) during the most
    /// recent <see cref="PurchaseAsync"/> attempt — including recovery of a stuck pending
    /// transaction that completed while waiting. Use this to avoid showing a hard failure
    /// after the player already received items.
    /// </summary>
    bool LastPurchaseGrantedRewards { get; }

    /// <summary>
    /// Triggers store restoration / purchases fetch for non-consumables and subscriptions.
    /// Fire-and-forget legacy convenience wrapper.
    /// </summary>
    void RestorePurchases();

    /// <summary>
    /// Triggers store restoration / purchases fetch for non-consumables and subscriptions
    /// and reports which configured restorable products were found among existing confirmed
    /// purchases.
    /// </summary>
    Task<RestorePurchasesResult> RestorePurchasesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the entitlement has already been granted and cached locally.
    /// </summary>
    /// <param name="entitlementId">Entitlement string (e.g. <c>no_ads</c>).</param>
    bool HasEntitlement(string entitlementId);

    /// <summary>
    /// Clears locally cached entitlements and persists an empty snapshot.
    /// Call on account wipe / new player. Store-owned non-consumables can be
    /// re-applied only via <see cref="RestorePurchases"/> / <see cref="RestorePurchasesAsync"/>.
    /// </summary>
    void ClearEntitlements();

    /// <summary>
    /// Tries to read store-localized metadata for a registered product.
    /// Returns false when the product has not been fetched yet or metadata is missing.
    /// </summary>
    /// <param name="productId"><see cref="RealMoneyProductDefinition.ProductId"/>.</param>
    /// <param name="info">Localized metadata when available.</param>
    bool TryGetProductInfo(string productId, out RealMoneyProductInfo info);

    /// <summary>
    /// Fired after a purchase has been processed successfully (including pending recovery).
    /// Argument = product id.
    /// </summary>
    event Action<string> PurchaseSucceeded;

    /// <summary>
    /// Fired after store product metadata becomes available or is refreshed.
    /// Use this to update buy-button price labels.
    /// </summary>
    event Action ProductsUpdated;
}
