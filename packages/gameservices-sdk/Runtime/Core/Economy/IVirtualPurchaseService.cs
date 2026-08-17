using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Portable wrapper over store-independent virtual purchases (UGS Economy Virtual Purchase).
/// Use for free bundles and soft-currency bundles configured in the Economy dashboard —
/// not for Apple/Google real-money IAP (<see cref="IRealMoneyPurchaseService"/>).
/// </summary>
public interface IVirtualPurchaseService
{
    /// <summary>
    /// Fired after a virtual purchase succeeds on the backend.
    /// Argument is the Economy virtual purchase id.
    /// </summary>
    event Action<string> PurchaseSucceeded;

    /// <summary>
    /// Makes the specified virtual purchase (single-flight; overlapping calls return false).
    /// Requires network; refreshes balances when an inventory service was provided to the implementation.
    /// </summary>
    /// <param name="purchaseId">Economy Virtual Purchase id from the UGS Dashboard (case-sensitive).</param>
    /// <param name="cancellationToken">Cancels the await; the underlying UGS call may still complete.</param>
    /// <returns>
    /// True on success; false when offline, busy, timed out, or rejected by Economy
    /// (insufficient funds, unknown id, etc.).
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="purchaseId"/> is null or whitespace.</exception>
    Task<bool> PurchaseAsync(string purchaseId, CancellationToken cancellationToken = default);
}
