# Remote Config

← [Back to README](../README.md)

---

## Interface: `IRemoteConfigService`

Exposed via `GameServicesLocator.Services.RemoteConfig` (nullable — null when not enabled or auth failed).

| Member | Description |
|--------|-------------|
| `IsReady` | Config is available (remote fetch and/or disk cache). |
| `UsedCacheOnly` | Latest load used only local cache (offline or fetch fallback). |
| `FetchAsync()` | Fetches from UGS when online; falls back to cache on failure. |
| `HasKey(key)` | Whether a key exists in live config or cache. |
| `GetString` / `GetJson` / `GetBool` / `GetInt` / `GetFloat` | Typed reads with defaults. |

Network errors throw `RemoteConfigOperationException` only when fetch fails **and** no cache exists.

---

## Bootstrap (opt-in)

```csharp
await new UGSServicesBuilder()
    .WithRemoteConfig()
    .OnAuthenticated(async _ =>
    {
        // Economy / Cloud Save ...
    })
    .BuildAsync();
```

`WithRemoteConfig()` fetches after successful auth. Values are persisted in PlayerPrefs (`remote_config_cached_values`) for offline reads on the next session.

---

## Reading values

```csharp
var rc = GameServicesLocator.Services?.RemoteConfig;
if (rc == null || !rc.IsReady)
    return;

int cap = rc.GetInt("inventory_max_cap", EconomyManager.InventoryMaxCap);
bool featureOn = rc.GetBool("feature_new_shop", false);
string json = rc.GetJson("economy_constants", "{}");
```

Use `GetJson` (not `GetString`) for Dashboard keys with type **json**.

---

## Mock (editor / tests)

`MockGameServices.CreateDefault()` includes `MockRemoteConfigService`:

```csharp
var mock = (MockRemoteConfigService)GameServicesLocator.Services.RemoteConfig;
mock.SetValue("inventory_max_cap", "8");
```

---

## Design notes

- **SDK only** — overlay in game `EconomyManager` is a separate step (game-side).
- Keys are case-insensitive in cache; UGS runtime is case-insensitive from 4.x.
- Re-fetch at runtime: `await Services.RemoteConfig.FetchAsync()` (e.g. on app resume).

---

## Dashboard setup

1. Link project in **Edit → Project Settings → Services**.
2. Deploy keys in **Unity Dashboard → Remote Config**.
3. Use the same key names in code (`GetJson("economy_constants")`, etc.).
