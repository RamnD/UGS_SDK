# Economy — Currency & Items

← [Back to README](../README.md)

---

## The mapper pattern — enum → string

All services that talk to external backends (UGS, server, etc.) use string IDs. Your game uses type-safe enums. **Mappers** are the single bridge between them — defined once per project, injected into services.

This way:
- Game code never contains magic strings like `"gold"` or `"high_score"`
- Renaming an enum value is a compile error, not a silent bug
- The backend ID can differ from the C# name without affecting game code

---

## Currency — IInventoryService\<TCurrency\>

### Step 1 — Define your enum

```csharp
// CurrencyType.cs  (in your project, not in SDK)
public enum CurrencyType
{
    Gold,
    Gems,
    Energy,
}
```

### Step 2 — Implement ICurrencyMapper\<TCurrency\>

The mapper owns **all** currency business rules: which string ID maps to which UGS resource, and which operations are allowed offline / on recoverable network failure.

```csharp
// CurrencyMapper.cs
public sealed class CurrencyMapper : ICurrencyMapper<CurrencyType>
{
    /// <summary>
    /// Must match the Resource ID in UGS Dashboard → Economy → Currencies.
    /// </summary>
    public string ToServiceId(CurrencyType currency) => currency switch
    {
        CurrencyType.Gold   => "GOLD",
        CurrencyType.Gems   => "GEMS",
        CurrencyType.Energy => "ENERGY",
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, null),
    };

    /// <summary>
    /// Offline / recoverable-network rules:
    ///   Add Gold/Energy  — allowed (optimistic cache + pending queue).
    ///   Spend Gold/Energy — allowed (same queue; net deltas are coalesced).
    ///   Add/Spend Gems   — requires server (premium; never grant or debit offline).
    /// </summary>
    public bool IsOfflineAllowed(CurrencyType currency, InventoryOperation op)
    {
        return currency switch
        {
            CurrencyType.Gold   => true,
            CurrencyType.Energy => true,
            CurrencyType.Gems   => false,   // premium — never mutate without server
            _ => false,
        };
    }
}
```

### Step 3 — Create the service in OnAuthenticated

```csharp
.OnAuthenticated(async auth =>
{
    _economy = new UGSEconomyService<CurrencyType>(new CurrencyMapper());
    await _economy.RefreshBalancesAsync(); // flush queue + sync with server
})
```

### Step 4 — Use in game code

```csharp
// Read balance (synchronous, cached — safe in Update / UI)
long gold = _economy.GetCachedBalance(CurrencyType.Gold);
goldLabel.text = gold.ToString();

// Award currency (e.g. after watching ad or completing level)
try
{
    await _economy.AddCurrencyAsync(CurrencyType.Gold, 100, destroyCancellationToken);
}
catch (InventoryOperationException ex)
{
    Debug.LogError($"Could not add gold: {ex.Reason}");
}

// Spend currency (returns false if insufficient / soft network failure)
bool spent = await _economy.TrySpendCurrencyAsync(CurrencyType.Gold, 50, destroyCancellationToken);
if (!spent)
    ShowNotEnoughGoldPopup();
```

### InventoryOperationException reference

| `InventoryFailureReason` | When |
|--------------------------|------|
| `NetworkUnavailable` | Reserved for explicit no-network hard failures |
| `OperationNotAllowedOffline` | Offline Add not allowed by mapper |
| `ProviderRejected` | Non-recoverable UGS / config error |
| `PendingTransactionsFlushFailed` | Queue flush hit a non-recoverable error |

`TrySpendCurrencyAsync` does **not** throw for insufficient funds or recoverable network errors — it returns `false` (or queues locally when the mapper allows spend offline).

---

## Durable pending queue

`UGSEconomyService` keeps:

| PlayerPrefs key | Purpose |
|-----------------|----------|
| `economy_cached_balances` | Last known balances (UI + offline boot) |
| `economy_pending_tx` | Coalesced signed deltas waiting for upload |

Legacy key `economy_pending_adds` is migrated automatically on first load.

### When deltas are queued

1. Device is offline (`NetworkStatus.IsOnline == false`) and the mapper allows the operation.
2. Device looks online, but the UGS call fails with a **recoverable** transport error (timeout, connection, 5xx-style messages) and the mapper allows the operation.

Queued amounts for the same currency are **coalesced** into one net delta (`+5` then `-2` → `+3`).

### When the queue flushes

Call `RefreshBalancesAsync()`:

- at sign-in / `OnAuthenticated`
- after reconnect
- on app resume (recommended)

Flow:

1. Flush pending deltas to UGS (stop on first recoverable failure; keep the remaining tail).
2. If anything is still pending — **keep local cache**, do not overwrite from server.
3. Otherwise `GetBalancesAsync` and replace the cache from the server.

---

## Items — IItemService\<TItem\>

Items are **persistent unlockables** (skins, power-ups, etc.) — not consumables. Purchasing deducts currency and grants an inventory item in one atomic UGS operation.

### Step 1 — Define item enum

```csharp
public enum ItemId
{
    SkinDefault,
    SkinFireSpirit,
    PowerUpMagnet,
}
```

### Step 2 — Implement IItemMapper\<TItem, TCurrency\>

```csharp
// ItemMapper.cs
public sealed class ItemMapper : IItemMapper<ItemId, CurrencyType>
{
    /// <summary>Must match the Virtual Purchase ID in UGS Dashboard → Economy → Purchases.</summary>
    public string ToServiceId(ItemId item) => item switch
    {
        ItemId.SkinDefault    => "SKIN_DEFAULT",
        ItemId.SkinFireSpirit => "SKIN_FIRE_SPIRIT",
        ItemId.PowerUpMagnet  => "POWERUP_MAGNET",
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };

    /// <summary>Used for UI display only — price shown to player before confirming purchase.</summary>
    public (CurrencyType currency, int amount) GetPrice(ItemId item) => item switch
    {
        ItemId.SkinDefault    => (CurrencyType.Gold, 0),
        ItemId.SkinFireSpirit => (CurrencyType.Gold, 5000),
        ItemId.PowerUpMagnet  => (CurrencyType.Gems, 100),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };
}
```

### Step 3 — Create service

```csharp
_items = new UGSItemService<ItemId, CurrencyType>(new ItemMapper(), _economy);
await _items.RefreshAsync();
```

### Step 4 — Use in game code

```csharp
// Check ownership (no network — cached)
bool hasSkin = _items.IsOwned(ItemId.SkinFireSpirit);

// Purchase
bool purchased = await _items.TryPurchaseAsync(ItemId.SkinFireSpirit, destroyCancellationToken);
if (purchased)
{
    PlayerPrefs.SetInt("selected_skin", (int)ItemId.SkinFireSpirit);
    RefreshSkinPreview();
}
```

---

## Offline / recoverable behaviour summary

| Operation | Gold / Energy (mapper allows) | Gems (mapper denies) | Items |
|-----------|:---:|:---:|:---:|
| `GetCachedBalance` | ✅ local cache | ✅ local cache | — |
| `RefreshBalancesAsync` offline | ✅ loads PlayerPrefs | ✅ loads PlayerPrefs | ✅ loads PlayerPrefs |
| `AddCurrencyAsync` offline / recoverable | ✅ optimistic + queue | ❌ throws | — |
| `TrySpendCurrencyAsync` offline / recoverable | ✅ optimistic + queue | ❌ returns false | — |
| `TryPurchaseAsync` offline | — | — | ❌ returns false |

Spend never throws for gameplay soft-failures. The pending queue flushes on the next successful `RefreshBalancesAsync` path (and is preserved across app restarts via PlayerPrefs).
