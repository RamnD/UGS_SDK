using System;
using UnityEngine.Purchasing;

/// <summary>
/// Cross-project product description used by the portable real-money purchase service.
/// Product IDs must match both the store configuration and the Economy Real Money Purchase ID.
/// </summary>
[Serializable]
public sealed class RealMoneyProductDefinition
{
    /// <summary>Store product id / Economy real money purchase id.</summary>
    public string ProductId;

    /// <summary>Unity IAP product type.</summary>
    public ProductType ProductType;

    /// <summary>
    /// If true, successful purchases are redeemed through Economy using the product id as purchase id.
    /// </summary>
    public bool RedeemWithEconomy = true;

    /// <summary>
    /// Optional local entitlements to grant after a successful redeem / purchase,
    /// e.g. "no_ads", "vip", "season_pass".
    /// </summary>
    public string[] GrantedEntitlementIds = Array.Empty<string>();

    /// <summary>
    /// Existing purchases fetched from the store should restore entitlements only for
    /// non-consumables/subscriptions or any explicitly restorable products.
    /// </summary>
    public bool RestoreEntitlementsFromExistingPurchases = true;
}
