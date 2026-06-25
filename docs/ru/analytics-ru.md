# Аналитика

← [README](../../README.md) | 🇬🇧 [English version](../analytics.md)

---

## Интерфейс: `IAnalyticsSystem`

Доступ через `GameServicesLocator.Services?.Analytics` (nullable — может быть null до входа или в офлайн-моках).

| Метод | Описание |
|-------|----------|
| `LogEvent<T>(T payload)` | Записывает типизированное событие. Поля с `[AnalyticsKey]` становятся параметрами. |
| `Flush()` | Немедленно отправляет накопленные события на сервер. Вызывать при паузе / выходе. |

---

## Определение событий

Каждое событие — `struct`, реализующий `IAnalyticsEvent`. Атрибут `[AnalyticsKey("имя_параметра")]` помечает поля, которые попадут в событие аналитики.

```csharp
// AnalyticsEvents.cs  (ваш проект)

public readonly struct RunCompletedEvent : IAnalyticsEvent
{
    public string EventName => "run_completed";

    [AnalyticsKey("level_index")]   public int   LevelIndex  { get; init; }
    [AnalyticsKey("run_time_sec")]  public float RunTimeSec  { get; init; }
    [AnalyticsKey("distance_m")]    public float DistanceM   { get; init; }
    [AnalyticsKey("was_death")]     public bool  WasDeath    { get; init; }
    [AnalyticsKey("score")]         public int   Score       { get; init; }
}

public readonly struct SkinSelectedEvent : IAnalyticsEvent
{
    public string EventName => "skin_selected";

    [AnalyticsKey("skin_id")]       public string SkinId     { get; init; }
    [AnalyticsKey("is_purchase")]   public bool   IsPurchase { get; init; }
}

public readonly struct AdWatchedEvent : IAnalyticsEvent
{
    public string EventName => "ad_watched";

    [AnalyticsKey("placement")]     public string Placement  { get; init; }
    [AnalyticsKey("completed")]     public bool   Completed  { get; init; }
}
```

---

## Запись событий

```csharp
var analytics = GameServicesLocator.Services?.Analytics;

// После окончания забега:
analytics?.LogEvent(new RunCompletedEvent
{
    LevelIndex = currentLevel,
    RunTimeSec = (float)elapsed.TotalSeconds,
    DistanceM  = player.DistanceTravelled,
    WasDeath   = player.IsDead,
    Score      = scoreManager.FinalScore,
});

// Когда игрок выбирает скин:
analytics?.LogEvent(new SkinSelectedEvent
{
    SkinId     = skin.Id,
    IsPurchase = wasBought,
});
```

`LogEvent` — синхронный: ставит событие в очередь для пакетной отправки. Его не нужно `await`ить.

---

## Отправка событий (Flush)

UGS Analytics автоматически отправляет события по таймеру. Вызывайте `Flush()` явно при паузе / выходе, чтобы события не потерялись:

```csharp
private void OnApplicationPause(bool paused)
{
    if (paused)
        GameServicesLocator.Services?.Analytics?.Flush();
}

private void OnApplicationQuit()
{
    GameServicesLocator.Services?.Analytics?.Flush();
}
```

---

## Рекомендации по дизайну событий

- **Одна структура — одно имя события.** Не используйте одну универсальную структуру для разных концепций.
- **`readonly struct` + `init`.** События — записи данных, должны быть неизменяемыми.
- **Без nullable-параметров.** Используйте `""`, `0`, `false` как значения по умолчанию.
- **Без персональных данных.** Не передавайте ID игроков, имена, данные устройства — UGS собирает их отдельно.
- **Префикс в именах событий.** `run_completed`, `ui_button_tapped`, `ad_watched` — избегает коллизий со встроенными событиями UGS.
- **Не дублируйте данные.** Если параметр можно вычислить из другого события — не добавляйте его. Аналитика должна быть минимально достаточной.
