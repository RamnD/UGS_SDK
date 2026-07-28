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
    /// Starts a purchase flow for a configured product.
    /// Returns true when the purchase has been processed successfully.
    /// Rejects with false if another purchase is already in flight (single-flight).
    /// </summary>
    /// <param name="productId"><see cref="RealMoneyProductDefinition.ProductId"/> (Economy / game key).</param>
    /// <param name="cancellationToken">Cancels waiting for the store callback.</param>
    /// <returns>True on success; false on cancel, failure, or busy reject.</returns>
    Task<bool> PurchaseAsync(
        string productId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the most recent <see cref="PurchaseAsync"/> returned false because the
    /// player cancelled the store sheet. False after success, a real failure, busy reject,
    /// or when the platform does not distinguish cancel.
    /// Valid to read immediately after <see cref="PurchaseAsync"/> returns false.
    /// </summary>
    bool LastPurchaseWasUserCancelled { get; }

    /// <summary>
    /// Triggers store restoration / purchases fetch for non-consumables and subscriptions.
    /// </summary>
    void RestorePurchases();

    /// <summary>
    /// Returns true if the entitlement has already been granted and cached locally.
    /// </summary>
    /// <param name="entitlementId">Entitlement string (e.g. <c>no_ads</c>).</param>
    bool HasEntitlement(string entitlementId);

    /// <summary>
    /// Tries to read store-localized metadata for a registered product.
    /// Returns false when the product has not been fetched yet or metadata is missing.
    /// </summary>
    /// <param name="productId"><see cref="RealMoneyProductDefinition.ProductId"/>.</param>
    /// <param name="info">Localized metadata when available.</param>
    bool TryGetProductInfo(string productId, out RealMoneyProductInfo info);

    /// <summary>
    /// Fired after a purchase has been processed successfully.
    /// Argument = product id.
    /// </summary>
    event Action<string> PurchaseSucceeded;

    /// <summary>
    /// Fired after store product metadata becomes available or is refreshed.
    /// Use this to update buy-button price labels.
    /// </summary>
    event Action ProductsUpdated;
}
