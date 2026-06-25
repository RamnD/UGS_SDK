# Economy — Currency & Items

← [Back to README](../README.md) | 🇷🇺 [Русская версия](ru/economy-ru.md)

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

The mapper owns **all** currency business rules: which string ID maps to which UGS resource, and which operations are allowed offline.

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
    /// Offline rules:
    ///   Add Gold/Energy — allowed offline (optimistic cache + pending queue).
    ///   Add Gems        — requires server (premium currency, never grant offline).
    ///   Spend anything  — always requires server confirmation.
    /// </summary>
    public bool IsOfflineAllowed(CurrencyType currency, InventoryOperation op)
    {
        if (op == InventoryOperation.Spend) return false;

        return currency switch
        {
            CurrencyType.Gold   => true,
            CurrencyType.Energy => true,
            CurrencyType.Gems   => false,   // premium — never grant without server
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
    await _economy.RefreshBalancesAsync(); // sync with server on startup
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

// Spend currency (requires network; returns false if insufficient funds)
bool spent = await _economy.TrySpendCurrencyAsync(CurrencyType.Gems, 50, destroyCancellationToken);
if (!spent)
    ShowNotEnoughGemsPopup();
```

### InventoryOperationException reference

| `InventoryFailureReason` | When |
|--------------------------|------|
| `Offline` | Offline + operation not allowed offline |
| `InsufficientFunds` | Balance < amount (server-confirmed) |
| `ServerError` | UGS returned an error |
| `NetworkError` | No response from server |
| `InvalidOperation` | Bad arguments (zero amount, etc.) |

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

## Offline behaviour summary

| Operation | Gold / Energy | Gems | Items |
|-----------|:---:|:---:|:---:|
| `GetCachedBalance` | ✅ local cache | ✅ local cache | — |
| `RefreshBalancesAsync` offline | ✅ loads from PlayerPrefs | ✅ loads from PlayerPrefs | ✅ loads from PlayerPrefs |
| `AddCurrencyAsync` offline | ✅ optimistic + pending queue | ❌ throws | — |
| `TrySpendCurrencyAsync` offline | ❌ returns false | ❌ returns false | — |
| `TryPurchaseAsync` offline | — | — | ❌ returns false |

The pending offline queue flushes automatically on the next successful `RefreshBalancesAsync`.
