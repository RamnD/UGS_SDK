using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Portable wrapper over store-independent virtual purchases (UGS Economy Virtual Purchase).
/// </summary>
public interface IVirtualPurchaseService
{
    /// <summary>
    /// Fired after a virtual purchase succeeds on the backend.
    /// </summary>
    event Action<string> PurchaseSucceeded;

    /// <summary>
    /// Makes the specified virtual purchase.
    /// Returns false when the purchase could not be completed.
    /// </summary>
    Task<bool> PurchaseAsync(string purchaseId, CancellationToken cancellationToken = default);
}
