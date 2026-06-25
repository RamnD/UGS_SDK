# Consumables vs Inventory Items (UGS + клиент)

← [README](../../README.md) | [Экономика](economy-ru.md) | 🇬🇧 См. также [economy.md](../economy.md)

---

## Краткий ответ (актуальная схема проекта)

| Ресурс | UGS Dashboard | Клиент |
|--------|---------------|--------|
| Soft / Gems / Life | Currency | `IInventoryService` / `PlayerData` |
| Shield, Nitro, TimeManipulator | **Currency** | `IConsumableItemService` / `UGSConsumableItemService` |
| Скины (план) | **Inventory item** | `IItemService` / `UGSItemService` |

**Consumable** в игре = доменная модель (charges, consume, UI «x3»).  
**Currency** в UGS = хранение quantity для расходников (как Life).

`UGSConsumableItemService` использует **`PlayerBalances`** (`GetBalancesAsync`, `IncrementBalanceAsync`, `DecrementBalanceAsync`), не `PlayerInventory`.

---

## Два слоя терминов

### Игра (domain)

| Ресурс | Семантика | Клиент |
|--------|-----------|--------|
| Скины | permanent unlock | `IItemService` → `IsOwned()` |
| Shield, Nitro, … | stackable consumable | `IConsumableItemService` → `GetQuantity()` / `TryConsumeAsync()` |

### UGS Economy 3.x (platform)

Типы ресурсов: Currency, Inventory item, Virtual purchase, Real money purchase.

Отдельного типа «Consumable» в SDK нет — расходники у нас заведены как **Currency** в Dashboard.

---

## Целевая схема

```
SoftCoin / HardGem / Life
  Dashboard: Currency
  Client:    IInventoryService / UGSEconomyService

Shield / Nitro / TimeManipulator
  Dashboard: Currency  (напр. ITEM_SHIELD — Currency ID в Dashboard)
  Client:    IConsumableItemService / UGSConsumableItemService
  API:       PlayerBalances
  Семантика: quantity = balance, consume = DecrementBalanceAsync

Skins (будущее)
  Dashboard: Inventory item
  Client:    IItemService / UGSItemService
  API:       PlayerInventory
  Семантика: binary ownership
```

**Не смешивать:** Shield через `IItemService.IsOwned()` — неверная модель.

---

## UGSConsumableItemService (реализация)

| Операция | UGS API | Офлайн |
|----------|---------|--------|
| `RefreshAsync` | `GetBalancesAsync` → фильтр consumable currency IDs | PlayerPrefs cache |
| `GetQuantity` | локальный кэш (из balance) | из кэша |
| `TryConsumeAsync` | `DecrementBalanceAsync` | `false` (pessimistic) |
| `TryGrantAsync` | `IncrementBalanceAsync` | optimistic cache, если `IsOfflineAllowed(Add)` |

Маппинг: `IConsumableItemMapper.ToServiceId` → **Currency ID** в Dashboard (см. `ItemKeys.ToUGSId`).

---

## Dashboard: чеклист

- [ ] `ITEM_SHIELD`, `ITEM_NITRO` — Type = **Currency** в Configuration  
- [ ] Currency ID **точно совпадает** с `ItemKeys.ToUGSId()`  
- [ ] Find Player → **Currencies** (не Inventory) — баланс 3 / 5 после grant  
- [ ] Virtual Purchase / награда: reward = начисление currency  
- [ ] Скины (когда появятся) — Inventory item + `IItemService`  

---

## Покупки и награды

| Действие | Сервис |
|----------|--------|
| Выдать 3 Shield | `IConsumableItemService.TryGrantAsync(Shield, 3)` |
| Списать 1 Shield в ране | `TryConsumeAsync` |
| Купить скин (позже) | `IItemService.TryPurchaseAsync` |

---

## Связанные файлы

| Слой | Файлы |
|------|--------|
| SDK | `IConsumableItemService.cs`, `IConsumableItemMapper.cs`, `UGSConsumableItemService.cs` |
| Editor | `DevConsumableItemService.cs` |
| Bridge | `PlayerConsumablesData.cs` |
| Gameplay | `IConsumableInventory.cs`, `ShieldConsumable.cs` |
| Маппинг | `ItemKeys.IsConsumable()`, `ItemKeys.IsOfflineAllowed()` |

---

## FAQ

### «Consumable в Dashboard — это не inventory item?»

Верно для **Shield/Nitro**: у вас это **Currency**. Inventory item зарезервирован под **скины** (unique owned asset).

### «Почему два сервиса валют?»

`IInventoryService` — «настоящие» валюты (монеты, гемы, жизни).  
`IConsumableItemService` — consumable charges с той же UGS-примитивой (balance), но отдельный домен и API на клиенте (не путать с магазином скинов).

### «Нужен ли split enum ConsumableItemId?»

Не обязателен. `ItemId` + `IsConsumable()` достаточно до масштабирования каталога.
