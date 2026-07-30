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
        ProductId = "COIN_PACK_SMALL",          // Economy Real Money Purchase id
        StoreProductId = "coin_pack_small",     // App Store / Play SKU
        ProductType = ProductType.Consumable,
        RedeemWithEconomy = true,
    },
    new RealMoneyProductDefinition
    {
        ProductId = "BUNDLE_STAGE_0",
        StoreProductId = "bundle_stage_0",
        ProductType = ProductType.Consumable,
        RedeemWithEconomy = true,
    },
    new RealMoneyProductDefinition
    {
        ProductId = "AD_BLOCK_FOREVER",
        StoreProductId = "ad_block_forever",
        ProductType = ProductType.NonConsumable,
        RedeemWithEconomy = true,               // validates via Economy
        GrantedEntitlementIds = new[] { "no_ads" },
        RestoreEntitlementsFromExistingPurchases = true,
    },
};
```

Economy Resource IDs are uppercase (`A-Z0-9_`). Apple/Google store product ids are configured separately on the Economy Real Money Purchase (`storeIdentifiers`) and may use a different case/format. Unity IAP must fetch/purchase with the **store** SKU; redeem uses the **Economy** id.

### Definition fields

| Field | Purpose |
|------|---------|
| `ProductId` | Economy real-money purchase id (game-facing key for `PurchaseAsync`) |
| `StoreProductId` | Apple/Google SKU for Unity IAP; when empty, falls back to `ProductId` |
| `ProductType` | Unity IAP product type |
| `RedeemWithEconomy` | If true, submit the receipt to UGS Economy using `ProductId` |
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
`RestoreEntitlementsFromExistingPurchases = true` re-grants its configured entitlements
from **`ConfirmedOrders` only** (not pending / deferred payments).

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

### User cancel vs real failure vs indeterminate

`PurchaseAsync` returns `false` for cancel, hard failure, and indeterminate (unless rewards were already granted — then it returns `true`). After it returns, read:

```csharp
bool ok = await _iap.PurchaseAsync(productId);
if (ok || _iap.LastPurchaseGrantedRewards)
{
    // Success UI / sync balances
    return;
}

switch (_iap.LastPurchaseOutcome)
{
    case RealMoneyPurchaseOutcome.Cancelled:
        // Store sheet dismissed — skip error UI.
        return;
    case RealMoneyPurchaseOutcome.Indeterminate:
        // Payment may have gone through — soft copy, refresh balances, do NOT say "no charges".
        return;
    default:
        // Hard failure — show error.
        break;
}
```

Pending recovery: `InitializeAsync` and every `PurchaseAsync` call `ProcessPendingPurchasesAsync` first so stuck Apple/Google orders redeem before a new store sheet opens. Games may also call `ProcessPendingPurchasesAsync` on app resume.

Economy idempotency:
- `INVALID_ALREADY_REDEEMED` → treated as success (refresh + entitlements + `ConfirmPurchase`) so a stuck pending after an interrupted redeem does not surface as `IAP_FAILED`.
- `INVALID_ANOTHER_PLAYER` → `ConfirmPurchase` only (clear store); no grant on the current account (typical after anonymous delete while the receipt stayed on device).

Notes:
- Flag / outcome reset at the start of each `PurchaseAsync`.
- Only set cancel when the failure still belongs to that in-flight request (`PurchaseFailureReason.UserCancelled`).
- Platforms that do not distinguish cancel leave the flag `false` (treat as failure).
- Purchases are **single-flight**: a second `PurchaseAsync` while one is open returns `false` immediately.

---

## Store product metadata (UI prices)

After `InitializeAsync`, Unity IAP fetches products asynchronously. When fetch succeeds:

- `AreProductsReady` becomes `true`
- `ProductsUpdated` fires
- `TryGetProductInfo(productId, out info)` returns localized price / title / currency

If the first fetch fails (offline at boot, store outage), call `EnsureProductsFetched()` on resume or before purchase — failure clears the latch so a retry is allowed. A purchase that finds no store product also kicks a refetch and returns `false`.

```csharp
if (!_iap.AreProductsReady)
    _iap.EnsureProductsFetched();
```

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

## Store-specific redeem (Apple vs Google)

`RedeemEconomyPurchaseAsync` detects the store from the pending order / unified receipt / platform, then dispatches:

| Store | Receipt source | Economy API |
|------|----------------|-------------|
| **Google Play** | Unified payload `{ json, signature }` from `order.Info.Receipt` / `product.receipt` only | `RedeemGooglePlayPurchaseAsync` |
| **Apple App Store** | App Receipt (order / Apple extended service / unified). Poll + `RefreshAppReceipt` if lagging (StoreKit 2) | `RedeemAppleAppStorePurchaseAsync` |

There is **no** cross-store fallback: Google never calls Apple refresh APIs, and a missing store name never defaults to Apple.

### Google dashboard checklist

- Play Console product ids = `StoreProductId`
- Economy Real Money Purchase ids = `ProductId`; Store connection → Google = same Play SKUs
- Unity Dashboard → Project Settings → **Google License Key** (required for server validation)
- Optional for prod: Unity IAP Receipt Obfuscator → `GooglePlayTangle.cs`

---

## Important constraints

- `ProductId` must match the Economy `Real Money Purchase` id (uppercase).
- `StoreProductId` must match the Apple/Google product id configured on that Economy purchase (`storeIdentifiers`) and in the store consoles.
- Rewards are **not** hardcoded in the SDK. Economy Dashboard remains the source of truth.
- The service assumes Unity IAP + Economy redeem, not a custom backend.
- Entitlements are a separate concept from Economy rewards; use them for things like `no_ads`.

---

## Threading

This service touches Unity IAP / UnityEngine APIs and should be created / used from the main thread, typically from your bootstrap path after auth.
