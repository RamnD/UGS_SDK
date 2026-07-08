# Achievements

← [Back to README](../README.md)

---

## Overview

`IAchievementService` is a portable achievement abstraction exposed via `GameServicesLocator.Services?.Achievements`.

The default UGS implementation stores achievement progress in **UGS Cloud Save** as a single JSON payload. This keeps the API backend-agnostic while still giving projects a ready-to-use implementation.

Achievement IDs are **project-defined string constants**. The SDK does not know game enums or platform-specific achievement catalogs.

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
- keeps an in-memory cache for runtime reads
- flushes mutations back to Cloud Save immediately when online
- keeps pending changes in memory if the device goes offline

This is intentionally **portable**, not a wrapper over platform-native achievements such as Google Play Games or Game Center.

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
