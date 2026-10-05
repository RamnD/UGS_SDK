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

## Dual backend (UGS + RamnD game-services)

From SDK **2.3.0** you can send the same typed events to Unity Analytics, to a self-hosted [game-services](https://github.com/RamnD/game-services) gateway, or both.

```csharp
await new UGSServicesBuilder()
    .WithAnalyticsBackend(
        AnalyticsBackendMode.Both,
        new RamndAnalyticsConfig(
            baseUrl: "http://localhost:3000",
            apiKey: "dev-maze-key"))
    .BuildAsync();
```

| Mode | Behaviour |
|------|-----------|
| `Ugs` (default) | Current behaviour — Unity Analytics only |
| `Ramnd` | Gateway only (`POST /v1/analytics/events` with `X-Api-Key`) |
| `Both` | Fan-out; failures are independent |

RamnD events include envelope fields (`event_id`, `event_timestamp`, `user_id`, `platform`, optional `player_id`) plus `[AnalyticsKey]` parameters. `user_id` is a stable install Guid in PlayerPrefs. `event_id` and `event_timestamp` are fixed when the event is queued. HTTP **200** and **207** remove the batch from the client queue. The gateway dedupes a replayed `event_id`.

Pending RamnD envelopes are stored in `ramnd-analytics-queue.json` under `Application.persistentDataPath`, stamped with the gateway base URL. A file written for a different gateway is not sent. The queue survives process exit, so an offline session or a dead gateway is retried on the next launch, when `NetworkStatus` comes back online, and on `Flush()` (pause / quit). One failed attempt ends that drain; there is no tight retry loop. The cap is 500 events; older events past the cap are dropped. HTTP **400** with a `rejected` list drops those envelopes. A **400** without that list (unknown app, bad body), **401**, **429**, and **5xx** stay queued.

`WithCachedAnalytics()` still applies only to the UGS leg. It is not replayed into RamnD.

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

### Non-player builds + production

If the game is compiled with `UGS_ENV_PRODUCTION` **and** running in the Unity Editor, a Standalone build, or WebGL, `UGSServicesBuilder` does **not** construct `UGSAnalyticSystem` and never calls `StartDataCollection`. Locator analytics is a no-op so session waits do not stall. Mobile player builds collect as usual.

The guard covers desktop because those builds are dev/QA machines: before it existed they landed in the production dataset as `PC_CLIENT` / `LINUX_CLIENT` sessions and skewed every per-user KPI.

### Environment isolation of the offline queue

UGS uploads buffered events to whatever environment the **current** session initialized with — events do not remember the environment they were recorded against. The disk-backed `PendingAnalyticsQueue` is therefore stamped with the resolved environment name. On load, a queue written under a different environment is dropped with a warning rather than replayed, so switching Build Profiles between runs cannot flush staging events into production.

### `ugs_player_id` on custom events

UGS Analytics adds top-level `unityPlayerID` only to **standard** events (`gameStarted`, `clientDevice`, …). **Custom** events (`RecordEvent(CustomEvent)`) use a different SDK code path and do not get that field automatically.

`UGSAnalyticSystem` therefore injects a custom parameter **`ugs_player_id`** (UGS Authentication player UUID) into every custom event after sign-in. Add this parameter once in the UGS Dashboard (string) and attach it to your custom event schemas to filter by authenticated player.

Do **not** name this parameter with a `unity` prefix — those names are reserved and cannot be created in Event Manager; events that send them fail schema validation.

`userID` remains the Analytics installation / external user id — it is separate from `ugs_player_id`.

---

## Design guidelines

- **One struct per event name.** Don't reuse a generic event struct for different concepts.
- **Use `readonly struct` + `init`.** Events are data records — immutable.
- **No nullable parameters.** Use `""` / `0` / `false` as defaults.
- **No PII in parameters.** Don't log player IDs, names, or device info — UGS collects device info separately.
- **Prefix event names.** `run_completed`, `ui_button_tapped`, `ad_watched` — avoids collisions if UGS adds built-in events.
