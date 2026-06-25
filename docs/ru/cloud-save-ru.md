# Cloud Save — Облачные сохранения

← [README](../../README.md) | 🇬🇧 [English version](../cloud-save.md)

---

## Обзор

`ICloudSaveService<TKey>` использует стратегию **local-first**:

| Операция | Где | Когда |
|----------|-----|-------|
| `Set` | Память + PlayerPrefs | Сразу, без сети |
| `PushToCloudAsync` | UGS Cloud Save | При паузе / выходе из приложения |
| `LoadAsync` | UGS Cloud Save | При старте / переподключении |

Разрешение конфликтов — **явное**: сервис никогда не перезаписывает данные молча. Если у обеих версий (локальной и облачной) разные временны́е метки — возвращается `SaveConflict`, и сервис ждёт вашего вызова.

---

## Шаг 1 — Определить enum ключей сохранений

```csharp
// SaveKey.cs  (в вашем проекте)
public enum SaveKey
{
    HighScore,
    TotalRuns,
    SelectedSkin,
    SfxEnabled,
    MusicEnabled,
}
```

---

## Шаг 2 — Реализовать ISaveKeyMapper\<TKey\>

Рекомендуется: `snake_case`-строки, совпадающие с именами ключей в UGS Cloud Save.

```csharp
// SaveKeyMapper.cs
public sealed class SaveKeyMapper : ISaveKeyMapper<SaveKey>
{
    public string ToCloudKey(SaveKey key) => key switch
    {
        SaveKey.HighScore    => "high_score",
        SaveKey.TotalRuns    => "total_runs",
        SaveKey.SelectedSkin => "selected_skin",
        SaveKey.SfxEnabled   => "sfx_enabled",
        SaveKey.MusicEnabled => "music_enabled",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };
}
```

> Держите ключи **стабильными** — изменение облачного ключа означает потерю этого поля у всех существующих игроков при следующем `LoadAsync`. При переименовании пишите шаг миграции: считайте старый ключ, запишите новый, удалите старый.

---

## Шаг 3 — Создать сервис в OnAuthenticated

```csharp
.OnAuthenticated(async auth =>
{
    _cloudSave = new UGSCloudSaveService<SaveKey>(new SaveKeyMapper());

    var conflict = await _cloudSave.LoadAsync();
    if (conflict.HasValue)
        await HandleConflictAsync(conflict.Value);
})
```

---

## Шаг 4 — Чтение и запись во время игры

`Set` и `Get` — **синхронные**, безопасны для вызова из `Update`, колбэков UI и т.д.

```csharp
// Запись (только локально — отправка в облако при паузе/выходе)
_cloudSave.Set(SaveKey.HighScore, newScore);
_cloudSave.Set(SaveKey.SelectedSkin, (int)chosenSkin);

// Чтение (из локального кэша)
int  highScore = _cloudSave.Get<int>(SaveKey.HighScore, defaultValue: 0);
bool sfxOn     = _cloudSave.Get<bool>(SaveKey.SfxEnabled, defaultValue: true);
int  skinId    = _cloudSave.Get<int>(SaveKey.SelectedSkin, defaultValue: 0);
```

Поддерживаемые типы `TValue`: `int`, `long`, `float`, `bool`, `string` и любые **JSON-сериализуемые** структуры или классы (Newtonsoft.Json).

---

## Шаг 5 — Отправка при паузе / выходе

```csharp
// В MonoBehaviour вашего менеджера сохранений:
private async void OnApplicationPause(bool paused)
{
    if (!paused) return;
    try
    {
        await _cloudSave.PushToCloudAsync();
    }
    catch (CloudSaveOperationException ex)
    {
        Debug.LogWarning($"[Save] Не удалось отправить в облако: {ex.Message}");
        // Данные в безопасности в PlayerPrefs — повторная попытка при следующем запуске
    }
}

private void OnApplicationQuit()
{
    // Fire-and-forget при выходе (лучшее усилие)
    _ = _cloudSave.PushToCloudAsync();
}
```

---

## Разрешение конфликтов

Конфликт возникает, когда у обеих версий (локальной и облачной) есть данные с **разными временны́ми метками** (разница > 1 секунды). Это происходит после игры на двух устройствах без синхронизации.

```csharp
var conflict = await _cloudSave.LoadAsync();

if (conflict.HasValue)
{
    // Спрашиваем игрока, какую версию оставить:
    bool keepCloud = await ShowConflictDialogAsync(
        localTime:  conflict.Value.LocalTimestamp,
        cloudTime:  conflict.Value.CloudTimestamp);

    if (keepCloud)
        _cloudSave.ApplyCloud();   // перезаписать локальные данные облачными
    else
        _cloudSave.KeepLocal();    // отбросить облачный снимок, оставить локальные
}
```

**Свойства `SaveConflict`:**

| Свойство | Тип | Описание |
|----------|-----|----------|
| `LocalTimestamp` | `DateTime` | UTC-время последнего локального `Set` / `PushToCloudAsync` |
| `CloudTimestamp` | `DateTime` | UTC-время последней успешной отправки в облако |

---

## Офлайн-поведение

- `Set` / `Get` — всегда работают (память + PlayerPrefs, без сети)
- `LoadAsync` офлайн — возвращает `null` (нет конфликта), локальные данные без изменений
- `PushToCloudAsync` офлайн — возвращается сразу без исключений

---

## Обработка ошибок

```csharp
try
{
    await _cloudSave.LoadAsync(ct);
}
catch (OperationCanceledException)
{
    throw; // передаём отмену выше
}
catch (CloudSaveOperationException ex)
{
    // Показать UI с кнопкой "Повторить"
    Debug.LogError($"[Save] Загрузка не удалась: {ex.Message}");
}
```
