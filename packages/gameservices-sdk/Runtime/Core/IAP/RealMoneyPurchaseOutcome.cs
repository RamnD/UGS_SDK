/// <summary>
/// Result of the most recent <see cref="IRealMoneyPurchaseService.PurchaseAsync"/> call.
/// </summary>
public enum RealMoneyPurchaseOutcome
{
    /// <summary>No purchase attempted yet, or outcome cleared.</summary>
    None = 0,

    /// <summary>Store + Economy redeem succeeded (or entitlements granted).</summary>
    Success = 1,

    /// <summary>Player dismissed the store sheet.</summary>
    Cancelled = 2,

    /// <summary>Hard failure — receipt invalid / Economy rejected. Safe to say no grant.</summary>
    Failed = 3,

    /// <summary>
    /// Payment may have succeeded on the store, but client could not confirm redeem
    /// (missing receipt, timeout, transport). Do not claim "no charges were made".
    /// Pending queue may still complete on next drain / retry.
    /// </summary>
    Indeterminate = 4,
}
