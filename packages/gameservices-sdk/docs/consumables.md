# Consumables — stackable items

← [Back to README](../README.md)

---

## Overview

`IConsumableItemService<TItem>` tracks **stackable** items (hints, shields, boosters) as quantities.
Permanent unlocks stay on [`IItemService<TItem>`](./economy.md) — do not mix the two.

UGS stores consumable quantities as **Economy currencies** (Dashboard → Economy → Currencies).
The game enum still uses item-shaped names; `IConsumableItemMapper<TItem>` maps them to currency IDs.

---

## Step 1 — Mapper

```csharp
public sealed class ConsumableMapper : IConsumableItemMapper<ItemId>
{
    public string ToServiceId(ItemId item) => item switch
    {
        ItemId.HintBook => "HINT_BOOK",
        ItemId.Shield   => "SHIELD",
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };

    public bool IsConsumable(ItemId item) =>
        item is ItemId.HintBook or ItemId.Shield;

    public bool IsOfflineAllowed(ItemId item, InventoryOperation op) =>
        op == InventoryOperation.Add; // grants may queue offline; consume requires online
}
```

---

## Step 2 — Service

```csharp
IConsumableItemService<ItemId> consumables =
    new UGSConsumableItemService<ItemId>(new ConsumableMapper());

await consumables.RefreshAsync();

int hints = consumables.GetQuantity(ItemId.HintBook);
bool spent = await consumables.TryConsumeAsync(ItemId.HintBook, 1);
bool granted = await consumables.TryGrantAsync(ItemId.Shield, 3);
```

Editor / offline tests:

```csharp
var consumables = new MockConsumableItemService<ItemId>();
consumables.SetQuantity(ItemId.HintBook, 5);
```

---

## Behaviour notes

| Op | Online | Offline / recoverable |
|----|--------|------------------------|
| `RefreshAsync` | Pull balances; flush pending grants | Load PlayerPrefs cache (no throw) |
| `TryConsumeAsync` | Decrement on server | Soft `false` (no local negative) |
| `TryGrantAsync` | Increment on server | Local + pending queue if mapper allows `Add` |

- **Single-flight mutations:** overlapping `TryConsumeAsync` / `TryGrantAsync` for the **same** item returns `false` while one is in flight.
- **`ClearLocalCache()`:** wipe quantities + pending grants on account delete / switch (see [auth.md](./auth.md)).

---

## Related

- Currency balances: [economy.md](./economy.md)
- Account wipe checklist: [auth.md](./auth.md)
