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
    /// Initializes the purchase service and registers all product definitions with the store.
    /// Safe to call multiple times.
    /// </summary>
    Task InitializeAsync(
        RealMoneyProductDefinition[] products,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a purchase flow for a configured product.
    /// Returns true when the purchase has been processed successfully.
    /// </summary>
    Task<bool> PurchaseAsync(
        string productId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers store restoration / purchases fetch for non-consumables and subscriptions.
    /// </summary>
    void RestorePurchases();

    /// <summary>
    /// Returns true if the entitlement has already been granted and cached locally.
    /// </summary>
    bool HasEntitlement(string entitlementId);

    /// <summary>
    /// Fired after a purchase has been processed successfully.
    /// Argument = product id.
    /// </summary>
    event Action<string> PurchaseSucceeded;
}
