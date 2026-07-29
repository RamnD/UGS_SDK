# Economy Purchase Catalog

← [Back to README](../README.md) · Related: [Economy](economy.md) · [Virtual Purchases](virtual-purchases.md) · [Real-money IAP](iap.md)

---

## Overview

`IEconomyPurchaseCatalog` exposes **read-only** UGS Economy purchase definitions for dynamic shop UI — virtual purchases (soft currency / free bundles) and real-money purchases (Apple / Google product mapping).

| Concern | API |
|---------|-----|
| Sync dashboard definitions | `RefreshAsync()` |
| Filter / list entries | `Query(PurchaseCatalogQuery)` |
| Lookup by Economy purchase id | `TryGet(purchaseId, out entry)` |
| Execute a purchase | `IVirtualPurchaseService` / `IRealMoneyPurchaseService` |

**Important:** localized real-money **prices** still come from Unity IAP via `IRealMoneyPurchaseService.TryGetProductInfo` — the catalog only provides Economy ids, rewards, and store product ids.

---

## Setup

Create the catalog after authentication (Economy must be available):

```csharp
IEconomyPurchaseCatalog _purchaseCatalog;

.OnAuthenticated(async auth =>
{
    _purchaseCatalog = new UGSEconomyPurchaseCatalog();
    await _purchaseCatalog.RefreshAsync();
});
```

Call `RefreshAsync()` again after reconnect or when you need fresh dashboard data (e.g. lobby entry).

---

## Query examples

```csharp
// All virtual coin packs whose id contains "SHOP"
var coinPacks = _purchaseCatalog.Query(new PurchaseCatalogQuery
{
    Kind = PurchaseCatalogKind.Virtual,
    IdContains = "SHOP",
    RewardResourceIds = new[] { "GOLD" },
    RewardMatch = PurchaseResourceMatchMode.Any,
});

// Real-money offers that grant gems
var gemOffers = _purchaseCatalog.Query(new PurchaseCatalogQuery
{
    Kind = PurchaseCatalogKind.RealMoney,
    RewardResourceIds = new[] { "GEMS" },
});

// Bundles that cost gold (virtual purchases only)
var goldBundles = _purchaseCatalog.Query(new PurchaseCatalogQuery
{
    CostResourceIds = new[] { "GOLD" },
    CostMatch = PurchaseResourceMatchMode.Any,
});

if (_purchaseCatalog.TryGet("SHOP_COINS_100", out var entry))
{
    foreach (var reward in entry.Rewards)
        Debug.Log($"{reward.ResourceId} x{reward.Amount}");
}
```

Filter semantics:

- All set filters are combined with **AND**.
- `IdContains` is case-insensitive substring match on the Economy purchase id.
- `RewardResourceIds` / `CostResourceIds` match against resolved Economy resource ids on reward / cost lines.
- `RewardMatch` / `CostMatch`: `Any` (default) = at least one id present; `All` = every listed id must appear.

---

## Entry shape

Each `PurchaseCatalogEntry` contains:

| Field | Description |
|-------|-------------|
| `Id`, `Name` | Economy purchase id and dashboard name |
| `Kind` | `Virtual` or `RealMoney` |
| `Costs` | Cost lines (virtual only) |
| `Rewards` | Reward lines |
| `AppleStoreId`, `GoogleStoreId` | Store product ids (real-money only) |

Each `PurchaseCatalogLine` has `ResourceId`, `Amount`, and `ResourceKind` (`Currency`, `InventoryItem`, or `Unknown`).

---

## Offline behaviour

- After a successful refresh, the in-memory cache is kept for the session.
- `RefreshAsync()` while offline logs a warning and returns without clearing the cache.
- `Query()` / `GetAll()` require `IsSynced == true` (throws if the catalog was never refreshed).

---

## Mock

For tests / offline editor flows:

```csharp
var mock = new MockEconomyPurchaseCatalog();
mock.SetEntries(new[]
{
    new PurchaseCatalogEntry(
        "TEST_PACK",
        "Test Pack",
        PurchaseCatalogKind.Virtual,
        costs: new[] { new PurchaseCatalogLine("GOLD", 50, PurchaseResourceKind.Currency) },
        rewards: new[] { new PurchaseCatalogLine("HINT", 1, PurchaseResourceKind.InventoryItem) }),
});
await mock.RefreshAsync();
```

---

## Related purchase execution

- Virtual: [`IVirtualPurchaseService`](virtual-purchases.md) — `PurchaseAsync(purchaseId)`
- Real money: [`IRealMoneyPurchaseService`](iap.md) — use `AppleStoreId` / `GoogleStoreId` with IAP, redeem via Economy

The catalog does **not** replace purchase services; it only drives UI layout and ids.
