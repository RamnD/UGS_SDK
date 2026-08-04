using System;

/// <summary>
/// Outcome of a restore-existing-purchases request.
/// </summary>
public sealed class RestorePurchasesResult
{
    /// <summary>True when the store restore/fetch completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// True when at least one configured product with
    /// <see cref="RealMoneyProductDefinition.RestoreEntitlementsFromExistingPurchases"/>
    /// matched a confirmed existing store purchase.
    /// </summary>
    public bool RestoredAnyEntitlements { get; init; }

    /// <summary>Configured SDK product ids that matched existing confirmed purchases.</summary>
    public string[] RestoredProductIds { get; init; } = Array.Empty<string>();

    /// <summary>Optional error text when <see cref="Success"/> is false.</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}
