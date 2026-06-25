# Cloud Save

← [Back to README](../README.md) | 🇷🇺 [Русская версия](ru/cloud-save-ru.md)

---

## Overview

`ICloudSaveService<TKey>` uses a **local-first** strategy:

| Operation | Where | When |
|-----------|-------|------|
| `Set` | Memory + PlayerPrefs | Immediate, no network |
| `PushToCloudAsync` | UGS Cloud Save | On pause / quit |
| `LoadAsync` | UGS Cloud Save | On startup / reconnect |

Conflict resolution is **explicit** — the service never silently overwrites data. If both local and cloud versions exist with different timestamps, it returns a `SaveConflict` and waits for your call.

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

```csharp
// In your save manager MonoBehaviour:
private async void OnApplicationPause(bool paused)
{
    if (!paused) return;
    try
    {
        await _cloudSave.PushToCloudAsync();
    }
    catch (CloudSaveOperationException ex)
    {
        Debug.LogWarning($"[Save] Could not push to cloud: {ex.Message}");
        // Data is safe in PlayerPrefs — will retry on next session
    }
}

private void OnApplicationQuit()
{
    // Fire-and-forget on quit (best-effort)
    _ = _cloudSave.PushToCloudAsync();
}
```

---

## Conflict resolution

A conflict occurs when both local and cloud have data with **different timestamps** (> 1 second apart). This happens after playing on two devices without syncing.

```csharp
var conflict = await _cloudSave.LoadAsync();

if (conflict.HasValue)
{
    // Ask player which version to keep:
    bool keepCloud = await ShowConflictDialogAsync(
        localTime:  conflict.Value.LocalTimestamp,
        cloudTime:  conflict.Value.CloudTimestamp);

    if (keepCloud)
        _cloudSave.ApplyCloud();   // overwrites local with cloud data
    else
        _cloudSave.KeepLocal();    // discards cloud snapshot, keeps local
}
```

**`SaveConflict` properties:**

| Property | Type | Description |
|----------|------|-------------|
| `LocalTimestamp` | `DateTime` | UTC time of last local `Set` / `PushToCloudAsync` |
| `CloudTimestamp` | `DateTime` | UTC time of last successful cloud push |

---

## Offline behaviour

- `Set` / `Get` — always work (memory + PlayerPrefs, no network)
- `LoadAsync` offline — returns `null` (no conflict), local data unchanged
- `PushToCloudAsync` offline — returns immediately without throwing

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
