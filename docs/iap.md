# Real Money Purchases (IAP)

← [Back to README](../README.md)

---

## Overview

`UGSRealMoneyPurchaseService<TKey, TCurrency>` is the portable bridge between:

- `Unity IAP` for store connection and receipts
- `UGS Economy` for real-money purchase redeem / reward granting
- `ICloudSaveService<TKey>` for local-first entitlement persistence
- `IInventoryService<TCurrency>` for optional post-redeem balance refresh

This lets each game keep only:

- its product ids
- its save key enum / mapper
- any game reaction to entitlements such as `no_ads`

The actual store / receipt / Economy redeem flow lives in the SDK.

---

## When to use it

Use this service when:

- products are configured in App Store / Google Play
- the same product ids exist in UGS Economy as `Real Money Purchase`
- rewards are defined in the Economy Dashboard
- you want non-consumable entitlements stored through your SDK cloud-save interface

Examples:

- coin packs redeemed through Economy
- bundles redeemed through Economy
- `no_ads` entitlement restored from existing purchases and cached locally

---

## Step 1 — Add a save key for entitlements

Store all SDK-managed entitlements under a single cloud save key:

```csharp
public enum SaveKey
{
    // ...
    IapEntitlements,
}
```

```csharp
public sealed class SaveKeyMapper : ISaveKeyMapper<SaveKey>
{
    public string ToCloudKey(SaveKey key) => key switch
    {
        // ...
        SaveKey.IapEntitlements => "iap_entitlements",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
```

---

## Step 2 — Define products

```csharp
using UnityEngine.Purchasing;

RealMoneyProductDefinition[] products =
{
    new RealMoneyProductDefinition
    {
        ProductId = "COIN_PACK_SMALL",
        ProductType = ProductType.Consumable,
        RedeemWithEconomy = true,
    },
    new RealMoneyProductDefinition
    {
        ProductId = "BUNDLE_STAGE_0",
        ProductType = ProductType.Consumable,
        RedeemWithEconomy = true,
    },
    new RealMoneyProductDefinition
    {
        ProductId = "AD_BLOCK_FOREVER",
        ProductType = ProductType.NonConsumable,
        RedeemWithEconomy = true,               // validates via Economy
        GrantedEntitlementIds = new[] { "no_ads" },
        RestoreEntitlementsFromExistingPurchases = true,
    },
};
```

### Definition fields

| Field | Purpose |
|------|---------|
| `ProductId` | Store product id and Economy real-money purchase id |
| `ProductType` | Unity IAP product type |
| `RedeemWithEconomy` | If true, submit the receipt to UGS Economy |
| `GrantedEntitlementIds` | Local entitlements to persist after success |
| `RestoreEntitlementsFromExistingPurchases` | Re-grant entitlements on restore / purchases fetch |

---

## Step 3 — Create the service after auth

```csharp
private IInventoryService<CurrencyType> _economy;
private ICloudSaveService<SaveKey> _cloudSave;
private IRealMoneyPurchaseService _iap;

.OnAuthenticated(async auth =>
{
    _economy = new UGSEconomyService<CurrencyType>(new CurrencyMapper());
    await _economy.RefreshBalancesAsync();

    _cloudSave = new UGSCloudSaveService<SaveKey>(new SaveKeyMapper());
    await _cloudSave.LoadAsync();

    _iap = new UGSRealMoneyPurchaseService<SaveKey, CurrencyType>(
        _cloudSave,
        SaveKey.IapEntitlements,
        _economy);

    await _iap.InitializeAsync(products);
})
```

### Why `IInventoryService<TCurrency>` is optional

If you pass `_economy`, the service automatically calls `RefreshBalancesAsync()` after a successful redeem.

If you do not use the SDK economy cache in a project, pass `null` and refresh your own state after `PurchaseSucceeded`.

---

## Step 4 — Buy products

```csharp
bool boughtCoins = await _iap.PurchaseAsync("COIN_PACK_SMALL");
bool boughtBundle = await _iap.PurchaseAsync("BUNDLE_STAGE_0");
bool boughtNoAds = await _iap.PurchaseAsync("AD_BLOCK_FOREVER");
```

The service:

1. starts the Unity IAP purchase
2. waits for `PendingOrder`
3. parses the store receipt
4. redeems it through UGS Economy using the product id
5. refreshes the optional economy cache
6. stores any configured entitlements in cloud save local cache
7. confirms the purchase to the store

---

## Step 5 — Check entitlements

```csharp
bool noAds = _iap.HasEntitlement("no_ads");
if (noAds)
{
    // skip rewarded / interstitial flows
}
```

Because entitlements are stored through `ICloudSaveService<TKey>`, the value is:

- available synchronously from local cache
- persisted to PlayerPrefs by the existing cloud save implementation
- uploaded to UGS Cloud Save on your normal push cycle

---

## Restore purchases

```csharp
_iap.RestorePurchases();
```

When existing purchases are fetched, any matching product with
`RestoreEntitlementsFromExistingPurchases = true` re-grants its configured entitlements.

This is primarily useful for:

- non-consumables
- subscriptions

Consumables should usually not grant entitlements from restore flows.

---

## Events

```csharp
_iap.PurchaseSucceeded += productId =>
{
    Debug.Log($"Purchased: {productId}");
};

_iap.ProductsUpdated += () =>
{
    if (_iap.TryGetProductInfo("COIN_PACK_SMALL", out RealMoneyProductInfo info))
        priceLabel.text = info.LocalizedPriceString;
};
```

Use `PurchaseSucceeded` if your UI wants to react to a completed purchase without coupling to the service internals.

Use `ProductsUpdated` / `TryGetProductInfo` to fill buy-button price labels from App Store / Google Play. Keep a prefab placeholder price as fallback until `AreProductsReady` is true or when `TryGetProductInfo` returns false.

---

## Store product metadata (UI prices)

After `InitializeAsync`, Unity IAP fetches products asynchronously. When fetch succeeds:

- `AreProductsReady` becomes `true`
- `ProductsUpdated` fires
- `TryGetProductInfo(productId, out info)` returns localized price / title / currency

```csharp
if (_iap.TryGetProductInfo(productId, out RealMoneyProductInfo info) && info.HasLocalizedPrice)
    buyButtonLabel.text = info.LocalizedPriceString;
// else keep the prefab / hardcoded placeholder
```

| Field | Typical use |
|------|-------------|
| `LocalizedPriceString` | Buy button label (preferred) |
| `LocalizedTitle` / `LocalizedDescription` | Optional; games usually keep their own localization |
| `IsoCurrencyCode` / `LocalizedPrice` | Analytics / debugging |

---

## Important constraints

- `ProductId` must match the Economy `Real Money Purchase` id.
- Rewards are **not** hardcoded in the SDK. Economy Dashboard remains the source of truth.
- The service assumes Unity IAP + Economy redeem, not a custom backend.
- Entitlements are a separate concept from Economy rewards; use them for things like `no_ads`.

---

## Threading

This service touches Unity IAP / UnityEngine APIs and should be created / used from the main thread, typically from your bootstrap path after auth.
