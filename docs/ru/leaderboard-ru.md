# Лидерборд

← [README](../../README.md) | 🇬🇧 [English version](../leaderboard.md)

---

## Интерфейс: `ILeaderboardService`

Доступ через `GameServicesLocator.Services.Leaderboards`.  
ID лидербордов — **строковые константы**. Определите их в одном месте в проекте:

```csharp
// LeaderboardIds.cs  (ваш проект)
public static class LeaderboardIds
{
    public const string AllTimeRun = "all_time_run";
    public const string WeeklyRun  = "weekly_run";
}
```

---

## Отправка результата

Вызывать после окончания забега (победа или проигрыш — зависит от дизайна):

```csharp
try
{
    await GameServicesLocator.Services.Leaderboards
        .SubmitScoreAsync(LeaderboardIds.AllTimeRun, runTimeSeconds, destroyCancellationToken);
}
catch (OperationCanceledException)
{
    throw;
}
catch (LeaderboardOperationException ex)
{
    Debug.LogWarning($"[Leaderboard] Не удалось отправить результат: {ex.Message}");
    // Некритично — показать тост и продолжить
}
```

`score` — тип `double` (нативный тип UGS). Для временны́х лидербордов передавайте **секунды** (например, `42.73`). Для целочисленных очков — `(double)score`.

---

## Получение топ-результатов

```csharp
IReadOnlyList<LeaderboardEntry> top;
try
{
    top = await GameServicesLocator.Services.Leaderboards
        .GetTopScoresAsync(LeaderboardIds.AllTimeRun, count: 50, destroyCancellationToken);
}
catch (LeaderboardOperationException ex)
{
    ShowOfflineFallback();
    return;
}

foreach (var entry in top)
{
    // entry.Rank        — позиция с 1
    // entry.PlayerId    — UUID игрока в UGS
    // entry.PlayerName  — никнейм (пустой, если игрок не задал)
    // entry.Score       — double
    leaderboardUI.AddRow(entry.Rank, entry.PlayerName, FormatTime(entry.Score));
}
```

Пустой список — корректный ответ (ещё нет результатов), это не ошибка.

---

## Получение записи текущего игрока

```csharp
var myEntry = await GameServicesLocator.Services.Leaderboards
    .GetPlayerEntryAsync(LeaderboardIds.AllTimeRun, destroyCancellationToken);

if (myEntry.HasValue)
    myRankLabel.text = $"#{myEntry.Value.Rank}  {FormatTime(myEntry.Value.Score)}";
else
    myRankLabel.text = "Ещё нет в рейтинге";
```

Возвращает `null`, если игрок ещё не отправлял результат для этого лидерборда.

---

## LeaderboardEntry

```csharp
public readonly struct LeaderboardEntry
{
    public int    Rank       { get; }   // начиная с 1
    public string PlayerId   { get; }   // UUID в UGS
    public string PlayerName { get; }   // "" если не задан
    public double Score      { get; }
}
```

---

## Обработка ошибок

`LeaderboardOperationException` оборачивает все ошибки UGS и сети. Специальных подтипов нет — используйте `ex.Message` для логирования и всегда предлагайте повтор или деградацию (лидерборд — некритичный функционал).

```csharp
catch (LeaderboardOperationException ex)
{
    Debug.LogWarning($"Лидерборд недоступен: {ex.Message}");
    ShowCachedLeaderboardOrHide();
}
```
