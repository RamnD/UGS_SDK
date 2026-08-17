# Economy Purchase Catalog

← [Back to README](../README.md) · Related: [Economy](economy.md) · [Virtual Purchases](virtual-purchases.md) · [Real-money IAP](iap.md)

---

## Responsibility split

| Layer | Owns |
|-------|------|
| **SDK (`IEconomyPurchaseCatalog`)** | Sync Economy VP/RMP definitions; expose id, name, costs, rewards, store ids, custom data; filter/query |
| **Game** | Online shop gate, presentation (icons/copy/discounts), localized IAP prices, purchase execution, post-buy sync, soft shops that are not Economy VPs |

The catalog is **read-only**. It does not buy, price, or unlock the shop.

---

## Overview

| Concern | API |
|---------|-----|
| Sync dashboard definitions | `RefreshAsync()` |
| Filter / list entries | `Query(PurchaseCatalogQuery)` |
| Lookup by Economy purchase id | `TryGet(purchaseId, out entry)` |
| Execute a purchase | `IVirtualPurchaseService` / `IRealMoneyPurchaseService` |

**Localized real-money prices** come from Unity IAP via `IRealMoneyPurchaseService.TryGetProductInfo` — not from this catalog.

---

## Setup

```csharp
IEconomyPurchaseCatalog _purchaseCatalog;

.OnAuthenticated(async auth =>
{
    _purchaseCatalog = new UGSEconomyPurchaseCatalog();
    await _purchaseCatalog.RefreshAsync();

    // Optional: refresh on GameServicesSync reconnect
    GameServicesSync.Register(GameServiceId.PurchaseCatalog, ct => _purchaseCatalog.RefreshAsync(ct));
});
```

Typical game policy: **lock the shop while offline / until `IsSynced`**. Then cold-start disk cache is unnecessary.

---

## Query examples

```csharp
if (!_purchaseCatalog.IsSynced)
    return; // shop locked

var lobbyPacks = _purchaseCatalog.Query(new PurchaseCatalogQuery
{
    Kind = PurchaseCatalogKind.Virtual,
    CustomDataContains = "\"section\":\"lobby\"",
    RewardResourceIds = new[] { "GOLD" },
});

var coinPacks = _purchaseCatalog.Query(new PurchaseCatalogQuery
{
    Kind = PurchaseCatalogKind.Virtual,
    IdContains = "SHOP",
    RewardResourceIds = new[] { "GOLD" },
    RewardMatch = PurchaseResourceMatchMode.Any,
});

if (_purchaseCatalog.TryGet("SHOP_COINS_100", out var entry))
{
    foreach (var reward in entry.Rewards)
        Debug.Log($"{reward.ResourceId} x{reward.Amount}");

    // Dashboard Custom Data JSON (section, sort, badge, …)
    Debug.Log(entry.CustomDataJson);
}
```

Filter semantics:

- All set filters are combined with **AND**.
- `IdContains` / `CustomDataContains` are case-insensitive substring matches.
- `RewardResourceIds` / `CostResourceIds` match resolved Economy resource ids.
- `RewardMatch` / `CostMatch`: `Any` (default) or `All`.
- When not synced, `Query` / `GetAll` / `GetVirtual` / `GetRealMoney` return **empty** (no throw). `TryGet` returns false.

---

## Entry shape

| Field | Description |
|-------|-------------|
| `Id`, `Name` | Economy purchase id and dashboard name |
| `Kind` | `Virtual` or `RealMoney` |
| `Costs` | Cost lines (virtual only) |
| `Rewards` | Reward lines |
| `AppleStoreId`, `GoogleStoreId` | Store product ids (real-money only) |
| `CustomDataJson` | Raw Custom Data from the Economy dashboard |

Each `PurchaseCatalogLine` has `ResourceId`, `Amount`, and `ResourceKind` (`Currency`, `InventoryItem`, or `Unknown`).

---

## Sync behaviour

- Catalog and virtual purchases share one Economy configuration sync (`UGSEconomyConfigurationSync`) — no duplicate network calls.
- `RefreshAsync` always forces a fresh config sync, then rebuilds the cache.
- If sync fails after a prior success, the **last good cache is kept** (no wipe).
- Offline: keeps last cache when present; otherwise stays unsynced.

---

## Mock

```csharp
var mock = new MockEconomyPurchaseCatalog();
mock.SetEntries(new[]
{
    new PurchaseCatalogEntry(
        "TEST_PACK",
        "Test Pack",
        PurchaseCatalogKind.Virtual,
        costs: new[] { new PurchaseCatalogLine("GOLD", 50, PurchaseResourceKind.Currency) },
        rewards: new[] { new PurchaseCatalogLine("HINT", 1, PurchaseResourceKind.InventoryItem) },
        customDataJson: "{\"section\":\"lobby\"}"),
});
await mock.RefreshAsync();
```

---

## Related purchase execution

- Virtual: [`IVirtualPurchaseService`](virtual-purchases.md)
- Real money: [`iap.md`](iap.md) — catalog supplies store ids; IAP supplies localized price
