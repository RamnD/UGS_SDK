using System;
using UnityEngine.Purchasing;

/// <summary>
/// Cross-project product description used by the portable real-money purchase service.
/// <see cref="ProductId"/> is the Economy Real Money Purchase id (uppercase).
/// <see cref="StoreProductId"/> is the Apple/Google store SKU (may differ in case/format).
/// </summary>
[Serializable]
public sealed class RealMoneyProductDefinition
{
    /// <summary>Economy real money purchase id (also the game-facing purchase key).</summary>
    public string ProductId;

    /// <summary>
    /// Apple App Store / Google Play product id used by Unity IAP.
    /// When empty, <see cref="ProductId"/> is used (backward compatible).
    /// </summary>
    public string StoreProductId;

    /// <summary>Resolved store SKU for Unity IAP fetch / purchase / restore.</summary>
    public string ResolvedStoreProductId =>
        string.IsNullOrWhiteSpace(StoreProductId) ? ProductId : StoreProductId;

    /// <summary>Unity IAP product type.</summary>
    public ProductType ProductType;

    /// <summary>
    /// If true, successful purchases are redeemed through Economy using <see cref="ProductId"/>.
    /// </summary>
    public bool RedeemWithEconomy = true;

    /// <summary>
    /// Optional local entitlements to grant after a successful redeem / purchase,
    /// e.g. "no_ads", "vip", "season_pass".
    /// </summary>
    public string[] GrantedEntitlementIds = Array.Empty<string>();

    /// <summary>
    /// When true, <see cref="IRealMoneyPurchaseService.RestorePurchases"/> /
    /// <see cref="IRealMoneyPurchaseService.RestorePurchasesAsync"/> re-grants
    /// <see cref="GrantedEntitlementIds"/> from confirmed store purchases
    /// (non-consumables / subscriptions). Automatic pending drain does not grant these.
    /// </summary>
    public bool RestoreEntitlementsFromExistingPurchases = true;
}
