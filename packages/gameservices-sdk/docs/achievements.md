# Achievements

← [Back to README](../README.md)

---

## Overview

`IAchievementService` is a portable achievement abstraction exposed via `GameServicesLocator.Services?.Achievements`.
Optional native platform mirroring is exposed separately via `GameServicesLocator.Services?.PlatformAchievements`.

The default UGS implementation stores achievement progress in **UGS Cloud Save** as a single JSON payload. This keeps the API backend-agnostic while still giving projects a ready-to-use implementation.

Achievement IDs are **project-defined string constants**. The SDK does not know game enums or platform-specific achievement catalogs.
The game remains the source of truth for unlock rules, rewards, and visibility.

---

## Interface: `IAchievementService`

```csharp
public interface IAchievementService
{
    bool TryGetState(string achievementId, out AchievementState state);
    IReadOnlyCollection<AchievementState> GetAllStates();

    Task SetProgressAsync(string achievementId, double currentProgress, double targetProgress,
        CancellationToken cancellationToken = default);

    Task IncrementProgressAsync(string achievementId, double deltaProgress, double targetProgress,
        CancellationToken cancellationToken = default);

    Task UnlockAsync(string achievementId, CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>Wipe in-memory state (account delete / switch).</summary>
    void ClearLocalCache();
}
```

### `AchievementState`

```csharp
public readonly struct AchievementState
{
    public string AchievementId { get; }
    public double CurrentProgress { get; }
    public double TargetProgress { get; }
    public bool IsUnlocked { get; }
    public DateTime? UnlockedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; }
}
```

---

## Enabling achievements

Achievements are **opt-in** on the UGS builder:

```csharp
var services = await new UGSServicesBuilder()
    .WithAchievements()
    .BuildAsync(destroyCancellationToken);
```

If auth succeeds, `Services.Achievements` becomes available. If auth fails or the module is not enabled, `Services.Achievements` is `null`.

To also mirror the same achievement ids to native store profiles, enable the platform bridge:

```csharp
var services = await new UGSServicesBuilder()
    .WithAchievements()
    .WithPlatformAchievements(new MyAchievementPlatformMapper())
    .BuildAsync(destroyCancellationToken);
```

`Services.PlatformAchievements` is:

- `null` if auth failed or the feature is not enabled
- a no-op bridge in Mock services
- a runtime-selected native bridge on device builds, or a no-op bridge when the current runtime has no supported native reporter:
  - Android -> Google Play Games
  - iOS -> Apple Game Center

---

## Typical usage

Define IDs in your game project:

```csharp
public static class AchievementIds
{
    public const string FirstWin = "first_win";
    public const string HundredStars = "hundred_stars";
}
```

Increment progress:

```csharp
await GameServicesLocator.Services.Achievements
    .IncrementProgressAsync(AchievementIds.HundredStars, deltaProgress: 3, targetProgress: 100);
```

Unlock directly:

```csharp
await GameServicesLocator.Services.Achievements
    .UnlockAsync(AchievementIds.FirstWin);
```

Mirror the same result to the native platform profile:

```csharp
await GameServicesLocator.Services.PlatformAchievements
    .ReportUnlockAsync(AchievementIds.FirstWin);
```

Read cached state:

```csharp
if (GameServicesLocator.Services?.Achievements?.TryGetState(AchievementIds.FirstWin, out var state) == true
    && state.IsUnlocked)
{
    ShowUnlockedBadge();
}
```

---

## Storage model

The default `UGSAchievementService`:

- loads achievement state from Cloud Save after auth (`WithAchievements()`)
- marks a **cloud baseline** only after a successful online load (or confirmed empty payload)
- keeps an in-memory cache for runtime reads
- flushes mutations back to Cloud Save when online **and** a cloud baseline exists (never overwrites cloud from a failed/empty warmup)
- merges local overlay onto cloud when baseline loads after offline edits
- keeps pending changes in memory if the device goes offline

This is intentionally **portable**, not a wrapper over platform-native achievements such as Google Play Games or Game Center.

---

## Platform bridge model

`IPlatformAchievementBridge` is a **best-effort mirror** for the current runtime platform:

```csharp
public interface IPlatformAchievementBridge
{
    Task ReportProgressAsync(string achievementId, double currentProgress, double targetProgress,
        CancellationToken cancellationToken = default);

    Task ReportUnlockAsync(string achievementId, CancellationToken cancellationToken = default);
    Task FlushAsync(CancellationToken cancellationToken = default);
    void ClearLocalCache();
}
```

The bridge:

- uses **game-defined** achievement ids as input
- maps them to platform ids through `IAchievementPlatformMapper`
- stores pending local report state in `PlayerPrefs`
- retries failed native reports on the next `FlushAsync` / reconnect sync
- never grants rewards or decides unlock rules

### Mapping interface

```csharp
public interface IAchievementPlatformMapper
{
    bool TryGetGooglePlayAchievementId(string achievementId, out string platformId);
    bool TryGetAppleGameCenterAchievementId(string achievementId, out string platformId);
}
```

### Recommended game-side flow

Use the portable store as the source of truth, then mirror outward:

```csharp
await services.Achievements.IncrementProgressAsync(id, delta, target, ct);
await services.PlatformAchievements.ReportProgressAsync(id, current, target, ct);

if (justUnlocked)
{
    await rewardService.GrantAsync(id, ct);
    await services.PlatformAchievements.ReportUnlockAsync(id, ct);
}
```

### Platform requirements

- **Android / Google Play Games:** requires the `Google.Play.Games` plugin in the consuming project and an authenticated GPGS session.
- **iOS / Apple Game Center:** requires Apple GameKit (`com.apple.unityplugin.gamekit`) and an authenticated `GKLocalPlayer`. The SDK sets `RAMND_HAS_APPLE_GAMEKIT` from the package.

If these prerequisites are missing, the bridge keeps pending reports locally and retries later without affecting the game-owned achievement state.

---

## Mock behavior

`MockGameServices.CreateDefault()` exposes `MockAchievementService` automatically:

```csharp
var services = MockGameServices.CreateDefault();
await services.Achievements.UnlockAsync("debug_achievement");
```

Mock achievements are stored in memory only and never touch UGS.

---

## Error handling

Achievement backend failures are wrapped in `AchievementOperationException`.

```csharp
try
{
    await GameServicesLocator.Services.Achievements.FlushAsync();
}
catch (AchievementOperationException ex)
{
    Debug.LogWarning($"Achievements unavailable: {ex.Message}");
}
```

Treat achievements as non-critical progression UX unless your game explicitly depends on them.
