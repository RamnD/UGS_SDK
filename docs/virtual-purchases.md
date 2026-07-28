# Virtual Purchases (Economy)

← [Back to README](../README.md) · Related: [Economy](economy.md) · [Real-money IAP](iap.md)

---

## Overview

`IVirtualPurchaseService` wraps **UGS Economy Virtual Purchases** — store-independent purchases configured in the Dashboard (free bundles, soft-currency bundles, “buy with Gold”, etc.).

| Use | API |
|-----|-----|
| Soft currency / free Economy purchases | `IVirtualPurchaseService` / `UGSVirtualPurchaseService<TCurrency>` |
| Apple / Google real money | `IRealMoneyPurchaseService` — see [iap.md](iap.md) |

Virtual purchases require network. They are **single-flight** (overlapping `PurchaseAsync` returns `false`).

---

## Setup

1. Create a **Virtual Purchase** in UGS Dashboard → Economy (costs + rewards).
2. Note the purchase **id** (case-sensitive string).
3. Create the service in `OnAuthenticated` (after Economy exists if you want balance refresh):

```csharp
.OnAuthenticated(async auth =>
{
    _economy = new UGSEconomyService<CurrencyType>(new CurrencyMapper());
    await _economy.RefreshBalancesAsync();

    _virtualPurchases = new UGSVirtualPurchaseService<CurrencyType>(_economy);
    _virtualPurchases.PurchaseSucceeded += id =>
        Debug.Log($"Virtual purchase ok: {id}");
})
```

Pass `null` instead of `_economy` if you refresh balances yourself after success.

---

## Usage

```csharp
bool ok = await _virtualPurchases.PurchaseAsync("STARTER_BUNDLE");
if (!ok)
{
    // Offline, busy, timeout, insufficient funds, unknown id, etc.
    // Map to UI from your own context — the SDK does not throw for soft failures.
}
```

| Result | Meaning |
|--------|---------|
| `true` | Purchase applied; `PurchaseSucceeded` fired; balances refreshed when economy was injected |
| `false` | Could not complete (offline / soft-offline, in-flight, timeout, Economy reject) |
| `ArgumentException` | Empty / whitespace `purchaseId` |

---

## Behaviour notes

- Lazy **Economy configuration sync** before the first purchase (and one retry on `ConfigNotSynced`).
- Bounded by `NetworkRequest` timeout (15s for the purchase call).
- Transport failures feed `NetworkStatus.ReportFailure` (soft circuit breaker).
- No offline queue for virtual purchases — unlike `IInventoryService` Add/Spend for mapper-allowed currencies.
