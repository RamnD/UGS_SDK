# Экономика — Валюта и Предметы

← [README](../../README.md) | 🇬🇧 [English version](../economy.md)

---

## Паттерн маппера: enum → string

Все сервисы, работающие с внешними бэкендами (UGS, сервер и т.д.), используют строковые идентификаторы. Игра работает с типобезопасными enum'ами. **Маппер** — единственный мост между ними: определяется один раз на проект и передаётся в сервисы через конструктор.

Преимущества:
- В игровом коде нет магических строк `"gold"`, `"high_score"` и т.д.
- Переименование значения enum → ошибка компилятора, а не тихий баг
- ID в бэкенде может отличаться от имени в C# — это не влияет на игровой код

---

## Валюта — IInventoryService\<TCurrency\>

### Шаг 1 — Определить enum

```csharp
// CurrencyType.cs  (в вашем проекте, не в SDK)
public enum CurrencyType
{
    Gold,
    Gems,
    Energy,
}
```

### Шаг 2 — Реализовать ICurrencyMapper\<TCurrency\>

Маппер владеет **всеми** бизнес-правилами валюты: какой enum соответствует какому ID в UGS, и какие операции разрешены офлайн.

```csharp
// CurrencyMapper.cs
public sealed class CurrencyMapper : ICurrencyMapper<CurrencyType>
{
    /// <summary>
    /// Должен совпадать с Resource ID в UGS Dashboard → Economy → Currencies.
    /// </summary>
    public string ToServiceId(CurrencyType currency) => currency switch
    {
        CurrencyType.Gold   => "GOLD",
        CurrencyType.Gems   => "GEMS",
        CurrencyType.Energy => "ENERGY",
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, null),
    };

    /// <summary>
    /// Правила офлайна:
    ///   Начислить Gold/Energy — разрешено офлайн (оптимистичный кэш + очередь).
    ///   Начислить Gems         — требует сервер (премиум-валюта, никогда не начисляем офлайн).
    ///   Списать что угодно     — всегда требует подтверждения сервера.
    /// </summary>
    public bool IsOfflineAllowed(CurrencyType currency, InventoryOperation op)
    {
        if (op == InventoryOperation.Spend) return false;

        return currency switch
        {
            CurrencyType.Gold   => true,
            CurrencyType.Energy => true,
            CurrencyType.Gems   => false,   // премиум — без сервера не выдаём
            _ => false,
        };
    }
}
```

### Шаг 3 — Создать сервис в OnAuthenticated

```csharp
.OnAuthenticated(async auth =>
{
    _economy = new UGSEconomyService<CurrencyType>(new CurrencyMapper());
    await _economy.RefreshBalancesAsync(); // синхронизация с сервером при старте
})
```

### Шаг 4 — Использование в игровом коде

```csharp
// Читаем баланс (синхронно, из кэша — безопасно в Update / UI)
long gold = _economy.GetCachedBalance(CurrencyType.Gold);
goldLabel.text = gold.ToString();

// Начисляем валюту (например, за просмотр рекламы или завершение уровня)
try
{
    await _economy.AddCurrencyAsync(CurrencyType.Gold, 100, destroyCancellationToken);
}
catch (InventoryOperationException ex)
{
    Debug.LogError($"Не удалось начислить золото: {ex.Reason}");
}

// Списываем (требует сети; возвращает false если не хватает средств)
bool spent = await _economy.TrySpendCurrencyAsync(CurrencyType.Gems, 50, destroyCancellationToken);
if (!spent)
    ShowNotEnoughGemsPopup();
```

### Справка по InventoryOperationException

| `InventoryFailureReason` | Когда |
|--------------------------|-------|
| `Offline` | Офлайн + операция не разрешена офлайн |
| `InsufficientFunds` | Баланс < суммы (подтверждено сервером) |
| `ServerError` | UGS вернул ошибку |
| `NetworkError` | Нет ответа от сервера |
| `InvalidOperation` | Некорректные аргументы (нулевая сумма и т.д.) |

---

## Предметы — IItemService\<TItem\>

Предметы — **постоянные разблокировки** (скины, усиления и т.д.), а не расходуемые ресурсы. Покупка списывает валюту и выдаёт предмет в одной атомарной операции UGS.

### Шаг 1 — Определить enum предметов

```csharp
public enum ItemId
{
    SkinDefault,
    SkinFireSpirit,
    PowerUpMagnet,
}
```

### Шаг 2 — Реализовать IItemMapper\<TItem, TCurrency\>

```csharp
// ItemMapper.cs
public sealed class ItemMapper : IItemMapper<ItemId, CurrencyType>
{
    /// <summary>Должен совпадать с Virtual Purchase ID в UGS Dashboard → Economy → Purchases.</summary>
    public string ToServiceId(ItemId item) => item switch
    {
        ItemId.SkinDefault    => "SKIN_DEFAULT",
        ItemId.SkinFireSpirit => "SKIN_FIRE_SPIRIT",
        ItemId.PowerUpMagnet  => "POWERUP_MAGNET",
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };

    /// <summary>Используется только для отображения цены в UI перед подтверждением покупки.</summary>
    public (CurrencyType currency, int amount) GetPrice(ItemId item) => item switch
    {
        ItemId.SkinDefault    => (CurrencyType.Gold, 0),
        ItemId.SkinFireSpirit => (CurrencyType.Gold, 5000),
        ItemId.PowerUpMagnet  => (CurrencyType.Gems, 100),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item, null),
    };
}
```

### Шаг 3 — Создать сервис

```csharp
_items = new UGSItemService<ItemId, CurrencyType>(new ItemMapper(), _economy);
await _items.RefreshAsync();
```

### Шаг 4 — Использование в игровом коде

```csharp
// Проверка владения (без сети — из кэша)
bool hasSkin = _items.IsOwned(ItemId.SkinFireSpirit);

// Покупка
bool purchased = await _items.TryPurchaseAsync(ItemId.SkinFireSpirit, destroyCancellationToken);
if (purchased)
{
    PlayerPrefs.SetInt("selected_skin", (int)ItemId.SkinFireSpirit);
    RefreshSkinPreview();
}
```

---

## Сводка по офлайн-поведению

| Операция | Gold / Energy | Gems | Предметы |
|----------|:---:|:---:|:---:|
| `GetCachedBalance` | ✅ локальный кэш | ✅ локальный кэш | — |
| `RefreshBalancesAsync` офлайн | ✅ загрузка из PlayerPrefs | ✅ загрузка из PlayerPrefs | ✅ загрузка из PlayerPrefs |
| `AddCurrencyAsync` офлайн | ✅ оптимистично + очередь | ❌ исключение | — |
| `TrySpendCurrencyAsync` офлайн | ❌ false | ❌ false | — |
| `TryPurchaseAsync` офлайн | — | — | ❌ false |

Очередь офлайн-начислений автоматически применяется при следующем успешном `RefreshBalancesAsync`.
