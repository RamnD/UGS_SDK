# Cloud Save

← [Back to README](../README.md)

---

## Overview

`ICloudSaveService<TKey>` uses a **local-first** strategy with optimistic concurrency:

| Operation | Where | When |
|-----------|-------|------|
| `Set` | Memory + PlayerPrefs | Immediate, no network |
| `PushToCloudAsync` | UGS Cloud Save | On pause / quit (returns conflict if cloud moved) |
| `LoadAsync` | UGS Cloud Save | On startup / reconnect |

| Timestamp | Meaning |
|-----------|---------|
| `LocalTimestamp` | Last local `Set` / successful push |
| `BaseTimestamp` | Cloud `__ts` from the last successful sync (parent version) |
| cloud `__ts` | Last successful push from any device |

Conflict resolution is **explicit** — the service never silently overwrites divergent data. Conflicts are reported via **return values** from `LoadAsync` / `PushToCloudAsync` (not events): await the call, show UI, then `ApplyCloud` or `KeepLocal`.

---

## Step 1 — Define save key enum

```csharp
// SaveKey.cs  (in your project)
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

## Step 2 — Implement ISaveKeyMapper\<TKey\>

Recommended: use `snake_case` strings matching UGS Cloud Save key names.

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

> Keep keys **stable** — changing a cloud key will cause all existing players to lose that field on next `LoadAsync`. If you rename, write a migration step in `LoadAsync` (read old key, write new key, delete old).

---

## Step 3 — Create service in OnAuthenticated

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

## Step 4 — Read & write during gameplay

`Set` and `Get` are **synchronous** and safe to call from `Update`, UI callbacks, etc.

```csharp
// Write (local only — pushed to cloud on pause/quit)
_cloudSave.Set(SaveKey.HighScore, newScore);
_cloudSave.Set(SaveKey.SelectedSkin, (int)chosenSkin);

// Read (from local cache)
int    highScore = _cloudSave.Get<int>(SaveKey.HighScore, defaultValue: 0);
bool   sfxOn     = _cloudSave.Get<bool>(SaveKey.SfxEnabled, defaultValue: true);
int    skinId    = _cloudSave.Get<int>(SaveKey.SelectedSkin, defaultValue: 0);
```

Supported `TValue` types: `int`, `long`, `float`, `bool`, `string`, and any **JSON-serializable** struct or class (Newtonsoft.Json).

---

## Step 5 — Push on pause / quit

Always check the return value — a second device may have written while you were playing.

```csharp
private async void OnApplicationPause(bool paused)
{
    if (!paused) return;
    try
    {
        var conflict = await _cloudSave.PushToCloudAsync();
        if (conflict.HasValue)
            await HandleConflictAsync(conflict.Value);
    }
    catch (CloudSaveOperationException ex)
    {
        Debug.LogWarning($"[Save] Could not push to cloud: {ex.Message}");
        // Data is safe in PlayerPrefs — will retry on next session
    }
}
```

---

## Conflict resolution

A conflict occurs when **local is dirty** (edits since `BaseTimestamp`) **and** cloud `__ts` moved away from `BaseTimestamp`. Typical case: two devices play from the same parent version, then both push.

```csharp
async Task HandleConflictAsync(SaveConflict conflict)
{
    bool keepCloud = await ShowConflictDialogAsync(
        localTime:  conflict.LocalTimestamp,
        cloudTime:  conflict.CloudTimestamp,
        source:     conflict.Source); // Load or Push

    if (keepCloud)
    {
        _cloudSave.ApplyCloud();
    }
    else
    {
        _cloudSave.KeepLocal();           // acknowledges cloud version
        var again = await _cloudSave.PushToCloudAsync();
        // again should be null after KeepLocal
    }
}
```

**Why return values (not a C# event):** the game must pause the sync flow, show UI, and only then continue. `await Load/Push → dialog → Apply/Keep` is the natural shape. An event would fire-and-forget and race with the next push.

**`SaveConflict` properties:**

| Property | Type | Description |
|----------|------|-------------|
| `LocalTimestamp` | `DateTime` | UTC time of last local `Set` / push |
| `CloudTimestamp` | `DateTime` | UTC time of the conflicting cloud `__ts` |
| `Source` | `SaveConflictSource` | `Load` or `Push` |
| `IsCloudNewer` | `bool` | `CloudTimestamp > LocalTimestamp` |

**Detection rules (summary):**

| Local dirty? | Cloud vs `BaseTimestamp` | Result |
|--------------|--------------------------|--------|
| No | cloud ahead | Auto `ApplyCloud` on Load; Push is no-op |
| No | same | In sync |
| Yes | same (parent unchanged) | Keep local edits; Push uploads |
| Yes | cloud moved | `SaveConflict` |

---

## Offline behaviour

- `Set` / `Get` — always work (memory + PlayerPrefs, no network)
- `LoadAsync` offline — returns `null` (no conflict), local data unchanged
- `PushToCloudAsync` offline — returns `null` immediately without throwing

---

## Error handling

```csharp
try
{
    await _cloudSave.LoadAsync(ct);
}
catch (OperationCanceledException)
{
    throw; // propagate cancellation
}
catch (CloudSaveOperationException ex)
{
    // Show retry UI
    Debug.LogError($"[Save] Load failed: {ex.Message}");
}
```
