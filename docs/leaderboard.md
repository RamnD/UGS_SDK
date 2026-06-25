# Leaderboard

← [Back to README](../README.md) | 🇷🇺 [Русская версия](ru/leaderboard-ru.md)

---

## Interface: `ILeaderboardService`

Exposed via `GameServicesLocator.Services.Leaderboards`.  
Leaderboard IDs are **string constants** — define them in one place in your project.

```csharp
// LeaderboardIds.cs  (your project)
public static class LeaderboardIds
{
    public const string AllTimeRun  = "all_time_run";
    public const string WeeklyRun   = "weekly_run";
}
```

---

## Submit a score

Call after a run ends (win or lose — depends on your design):

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
    Debug.LogWarning($"[Leaderboard] Submit failed: {ex.Message}");
    // Non-critical — show a toast and continue
}
```

`score` is `double` (UGS native). For time-based boards pass **seconds** (e.g. `42.73`).  
For integer scores cast with `(double)score`.

---

## Fetch top scores

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
    // entry.Rank        — 1-based position
    // entry.PlayerId    — UGS player UUID
    // entry.PlayerName  — display name (empty if player never set one)
    // entry.Score       — double
    leaderboardUI.AddRow(entry.Rank, entry.PlayerName, FormatTime(entry.Score));
}
```

An empty list is a valid response (no scores yet — not an error).

---

## Fetch current player's entry

```csharp
var myEntry = await GameServicesLocator.Services.Leaderboards
    .GetPlayerEntryAsync(LeaderboardIds.AllTimeRun, destroyCancellationToken);

if (myEntry.HasValue)
    myRankLabel.text = $"#{myEntry.Value.Rank}  {FormatTime(myEntry.Value.Score)}";
else
    myRankLabel.text = "Not ranked yet";
```

Returns `null` if the player has never submitted a score for this leaderboard.

---

## LeaderboardEntry

```csharp
public readonly struct LeaderboardEntry
{
    public int    Rank       { get; }   // 1-based
    public string PlayerId   { get; }   // UGS UUID
    public string PlayerName { get; }   // "" if not set
    public double Score      { get; }
}
```

---

## Error handling

`LeaderboardOperationException` wraps all UGS and network errors. It has no special sub-types — use `ex.Message` for logging and always offer a retry or graceful degradation (leaderboard is non-critical game functionality).

```csharp
catch (LeaderboardOperationException ex)
{
    Analytics.LogWarning($"Leaderboard unavailable: {ex.Message}");
    ShowCachedLeaderboardOrHide();
}
```
