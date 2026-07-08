# Analytics

← [Back to README](../README.md)

---

## Interface: `IAnalyticsSystem`

Exposed via `GameServicesLocator.Services?.Analytics` (nullable — may be null before sign-in or in offline mocks).

| Method | Description |
|--------|-------------|
| `LogEvent<T>(T payload)` | Records a typed event. Fields marked `[AnalyticsKey]` become event parameters. |
| `Flush()` | Immediately sends queued events to the server. Call on pause / quit. |

---

## Defining events

Each event is a `struct` implementing `IAnalyticsEvent`. Use `[AnalyticsKey("key_name")]` to mark the fields that should appear as event parameters.

```csharp
// AnalyticsEvents.cs  (your project)

public readonly struct RunCompletedEvent : IAnalyticsEvent
{
    public string EventName => "run_completed";

    [AnalyticsKey("level_index")]   public int    LevelIndex  { get; init; }
    [AnalyticsKey("run_time_sec")]  public float  RunTimeSec  { get; init; }
    [AnalyticsKey("distance_m")]    public float  DistanceM   { get; init; }
    [AnalyticsKey("was_death")]     public bool   WasDeath    { get; init; }
    [AnalyticsKey("score")]         public int    Score       { get; init; }
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

## Logging events

```csharp
var analytics = GameServicesLocator.Services?.Analytics;

// After a run ends:
analytics?.LogEvent(new RunCompletedEvent
{
    LevelIndex = currentLevel,
    RunTimeSec = (float)elapsed.TotalSeconds,
    DistanceM  = player.DistanceTravelled,
    WasDeath   = player.IsDead,
    Score      = scoreManager.FinalScore,
});

// When a player picks a skin:
analytics?.LogEvent(new SkinSelectedEvent
{
    SkinId     = skin.Id,
    IsPurchase = wasBought,
});
```

`LogEvent` is synchronous — it queues the event for batched sending. Never `await` it.

---

## Flushing events

UGS Analytics auto-flushes periodically. Call `Flush()` explicitly on app pause / quit so events aren't lost:

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

## Offline event cache (opt-in)

Enable disk-backed replay while offline via bootstrap:

```csharp
await new UGSServicesBuilder()
    .WithCachedAnalytics()
    .BuildAsync();
```

`CachedAnalyticsSystem` stores pending events in PlayerPrefs and replays them on the next online `LogEvent` / `Flush()`.

With `WithCachedAnalytics()`, analytics is registered in `GameServicesLocator` **before** auth completes. Events emitted during sign-in are queued and replayed after `AttachInner` connects the UGS backend.

### `unity_player_id` on custom events

UGS Analytics adds top-level `unityPlayerID` only to **standard** events (`gameStarted`, `clientDevice`, …). **Custom** events (`RecordEvent(CustomEvent)`) use a different SDK code path and do not get that field automatically.

`UGSAnalyticSystem` therefore injects a custom parameter **`unity_player_id`** (UGS Authentication player UUID) into every custom event after sign-in. Add this parameter once in the UGS Dashboard (string) and attach it to your custom event schemas to filter by authenticated player.

`userID` remains the Analytics installation / external user id — it is separate from `unity_player_id`.

---

## Design guidelines

- **One struct per event name.** Don't reuse a generic event struct for different concepts.
- **Use `readonly struct` + `init`.** Events are data records — immutable.
- **No nullable parameters.** Use `""` / `0` / `false` as defaults.
- **No PII in parameters.** Don't log player IDs, names, or device info — UGS collects device info separately.
- **Prefix event names.** `run_completed`, `ui_button_tapped`, `ad_watched` — avoids collisions if UGS adds built-in events.
