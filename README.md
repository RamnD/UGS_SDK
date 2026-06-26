# RamnD Game Services SDK

Portable façade over **Unity Gaming Services (UGS)** and test **mocks**. Your game code talks to **interfaces** in `Runtime/Core` and swaps **UGS** vs **Mock** at bootstrap.

**Runtime logs** from this package are in **English** (searchable, tooling-friendly). XML doc comments and documentation are also in English.

---

## Installation (UPM)

Add to your project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ramnd.gameservices-sdk": "https://github.com/RamnD/UGS_SDK.git#v1.5.0"
  }
}
```

Local development (sibling folder):

```json
"com.ramnd.gameservices-sdk": "file:../UGS_SDK"
```

After install, Unity pulls UGS / LevelPlay dependencies from this package's `package.json` automatically.

**Android Google Play sign-in** also requires [Google Play Games Plugin](#android--google-play-games-plugin-for-unity) in the consuming project (`Google.Play.Games` assembly). iOS Sign in with Apple plugin is optional until you wire `NativeAppleSignIn` (see [docs/auth.md](docs/auth.md)).

Optional sample: **Package Manager → RamnD Game Services SDK → Samples → Import Bootstrap**.

---

## Table of Contents

| Topic | Documentation |
|-------|---------------|
| Initialization & bootstrap | [docs/bootstrap.md](docs/bootstrap.md) |
| Auth & player name | [docs/auth.md](docs/auth.md) |
| Economy (currency & items) | [docs/economy.md](docs/economy.md) |
| Cloud Save | [docs/cloud-save.md](docs/cloud-save.md) |
| Leaderboard | [docs/leaderboard.md](docs/leaderboard.md) |
| Analytics | [docs/analytics.md](docs/analytics.md) |
| Remote Config | [docs/remote-config.md](docs/remote-config.md) |
| Security & credentials | [below](#security--credentials) |

> **Quick start:** see [docs/bootstrap.md](docs/bootstrap.md) or the condensed summary below.

---

## Security & credentials

This package is **public and contains no game-specific secrets**. Every consuming project supplies its own keys at runtime or via project settings. **Do not** commit credentials into this SDK repository.

### What lives where

| Credential / config | Owned by | How it is supplied |
|---------------------|----------|-------------------|
| UGS project link (Environment, Project ID) | Your game | **Edit → Project Settings → Services** (Unity Dashboard) |
| Google Play Games OAuth **Web Client ID** | Your game | GPGS plugin setup **and/or** `GameServicesAuthProviderConfig.GooglePlayGamesOAuthWebClientId` |
| Apple **Services ID** | Your game | `GameServicesAuthProviderConfig.AppleServicesId` |
| LevelPlay **App Key** | Your game | `new LevelPlayAdsManager("your-app-key")` in `.WithAds(...)` |
| Profanity / name rules | Your game | `WithNameValidator(...)` or `WithProfanityFilter(...)` |

Pass platform credentials through the builder (from a ScriptableObject, Remote Config, or build-time constants in **your** repo — not hardcoded in the SDK):

```csharp
.WithAuthProviderCredentials(new GameServicesAuthProviderConfig
{
    GooglePlayGamesOAuthWebClientId = gameConfig.GoogleWebClientId,
    AppleServicesId                 = gameConfig.AppleServicesId,
})
.WithAds(new LevelPlayAdsManager(gameConfig.LevelPlayAppKey))
```

### Public vs secret

| Value | Treat as | Safe in client / git? |
|-------|----------|------------------------|
| OAuth Web Client ID (Google) | Public client identifier | Yes |
| Apple Services ID | Public app identifier | Yes |
| LevelPlay App Key | App-scoped config | Yes in shipped builds; avoid leaking in public **server** repos if you treat it as sensitive |
| UGS session tokens, auth codes, identity tokens | Secret / ephemeral | **Never** log or commit |
| OAuth client **secret**, service account JSON, `.p12`, keystores | Secret | **Never** in SDK or game git — CI / secret store only |

The SDK logs **diagnostic** messages (PlayerId, errors). It does **not** print auth codes or identity tokens. Avoid adding debug logs that dump tokens in your game bootstrap.

### Never commit in your **game** project

Add patterns like these to your game `.gitignore`:

```gitignore
# Credentials & platform secrets
**/GoogleService-Info.plist
**/google-services.json
*.keystore
*.jks
*.p12
*.pem
*Credentials*.asset
*Secrets*.asset
.env
.env.*
```

Keep credential ScriptableObjects local, or load IDs from environment variables in CI for builds.

### Platform auth status

| Platform | Status in this SDK |
|----------|-------------------|
| Anonymous UGS | Supported |
| Google Play Games → UGS (Android) | Supported (requires [GPGS plugin](#android--google-play-games-plugin-for-unity) in the host project) |
| Sign in with Apple → UGS (iOS) | **Not implemented** — `UGSAuthService` throws until you wire `identityToken` via the Apple plugin (see [docs/auth.md](docs/auth.md)). Use `.WithForceAnonymous(true)` until then. |

### Disclaimer

**RamnD Game Services SDK** is an independent open-source helper for Unity Gaming Services. It is **not** affiliated with, endorsed by, or sponsored by Unity Technologies. Unity, Unity Gaming Services, and LevelPlay are trademarks of their respective owners.

### License

Distributed under the [MIT License](LICENSE).

---

## External platform plugins

These plugins are **not available in the Unity Package Manager registry** and must be imported manually as `.unitypackage` files (or via a git URL in the UPM manifest).

### Android — Google Play Games Plugin for Unity

| | |
|---|---|
| **Version** | **2.1.0** |
| **Release page** | https://github.com/playgameservices/play-games-plugin-for-unity/releases/tag/v2.1.0 |
| **Direct download** | [`GooglePlayGamesPlugin-2.1.0.unitypackage`](https://github.com/playgameservices/play-games-plugin-for-unity/releases/download/v2.1.0/GooglePlayGamesPlugin-2.1.0.unitypackage) |
| **UPM git URL** | `https://github.com/playgameservices/play-games-plugin-for-unity.git?path=com.google.play.games` |

**Setup steps:**
1. Download the `.unitypackage` and import via **Assets → Import Package → Custom Package**.
2. In Unity: **Window → Google Play Games → Setup → Android Setup** — enter your OAuth Web Client ID.
3. Set `GooglePlayGamesOAuthWebClientId` in `GameServicesAuthProviderConfig` (see [docs/auth.md](docs/auth.md)).

> Compatible with the `Authenticate(Action<SignInStatus>)` + `RequestServerSideAccess(bool, Action<string>)` API introduced in v2.1.0.

---

### iOS — Apple Sign In for Unity (lupidan)

| | |
|---|---|
| **Version** | **1.4.2** |
| **Release page** | https://github.com/lupidan/apple-signin-unity/releases/tag/v1.4.2 |
| **Direct download** | [`AppleSignIn-1.4.2.unitypackage`](https://github.com/lupidan/apple-signin-unity/releases/download/v1.4.2/AppleSignIn-1.4.2.unitypackage) |
| **UPM git URL** | `https://github.com/lupidan/apple-signin-unity.git` |

**Setup steps:**
1. Import the `.unitypackage`.
2. Enable the **Sign In with Apple** capability in Xcode (added automatically by the plugin's post-process build step).
3. Implement `NativeAppleSignIn.GetIdentityTokenAsync(ct)` (see TODO in `UGSAuthService.cs`) using `IAppleAuthManager` from the plugin.
4. Set `AppleServicesId` in `GameServicesAuthProviderConfig` (see [docs/auth.md](docs/auth.md)).

---

## Package layout

| Area | Path | Assembly |
|------|------|----------|
| Contracts, exceptions, locator | `Runtime/Core` | `RamnD.GameServices.Core` (`.asmdef`) |
| Mocks | `Runtime/Mock` | `RamnD.GameServices.Mock` (references Core + Newtonsoft.Json) |
| UGS implementations | `Runtime/UGS` | `RamnD.GameServices.UGS` (references Core, UGS packages, LevelPlay, `Google.Play.Games`) |

**Note:** `Google.Play.Games` must be present in the consuming project for Android auth (see below). Legacy Unity Ads (`UnityAdsManager`) compiles only with scripting define `RAMND_LEGACY_UNITY_ADS` plus `com.unity.ads` in the host project.

Dependencies are listed in `package.json` (Newtonsoft, Authentication, Economy, Cloud Save, Leaderboards, Analytics, Remote Config, Ads / LevelPlay).

---

## Single entry point after init

`UGSServicesBuilder.BuildAsync` and `MockGameServices.CreateDefault` register the façade in:

```csharp
GameServicesLocator.Set(services);
```

Use:

- `GameServicesLocator.Services` — `IGameServices` (null until `Set`)
- `GameServicesLocator.TryGet(out var services)` — safe check
- Analytics: `GameServicesLocator.Services?.Analytics?.LogEvent(...)` (null if not signed in)
- Ads: `GameServicesLocator.Services.Ads` (non-null after build)
- Leaderboards: `Services.Leaderboards` (null if not authenticated)
- Remote Config: `Services.RemoteConfig` (null if not enabled or not authenticated)

Generic services (economy, items, cloud save) stay **outside** the façade: inject `IInventoryService<T>`, `IItemService<T>`, `ICloudSaveService<TKey>` from your own bootstrap (see UGS example below).

---

## Option A — UGS (production)

1. Ensure UGS project IDs / packages are configured in Unity (**Project Settings → Services**).

2. From a `MonoBehaviour` (e.g. `ServicesBootstrap`), after the scene loads:

```csharp
private async void Start()
{
    var services = await new UGSServicesBuilder()
        .WithForceAnonymous(/* dev: true */ false)
        .WithNameValidator(/* optional */ myNameValidatorFromScriptableObject?.ToValidatorConfig())
        .WithAuthProviderCredentials(new GameServicesAuthProviderConfig
        {
            GooglePlayGamesOAuthWebClientId = "...",
            AppleServicesId = "...",
        })
        .WithAds(new LevelPlayAdsManager("your-app-key")) // or TestAdsManager / UnityAdsManager
        .OnAuthenticated(async auth =>
        {
            // Create UGSEconomyService<T>, UGSItemService<,>, UGSCloudSaveService<TKey>, wire to your MonoBehaviour bridges
        })
        .BuildAsync();

    // GameServicesLocator is set inside BuildAsync
}
```

3. **Cancellation:** pass `CancellationToken` into `BuildAsync(ct)` when you have one (e.g. destroy token).

4. **Network testing:** `NetworkStatus.ForceOffline = true` forces offline behaviour for services that respect `NetworkStatus.IsOnline`.

---

## Option B — Mock (no UGS / editor smoke tests)

```csharp
var services = MockGameServices.CreateDefault();
// GameServicesLocator is set inside CreateDefault(); Auth is signed in, Analytics/Ads/Leaderboards/RemoteConfig are mocks
```

Use mocks for `IInventoryService<T>` etc. (`MockInventoryService`, `MockItemService`, …) and pass them to your same `PlayerData`-style bridges — no change to UI code paths.

---

## Errors and localization

- **Do not** ship user-facing Russian/English strings from SDK log lines to players. Logs are for developers.
- For UI, map **`InventoryFailureReason`**, **`NameValidationError`**, **`LeaderboardOperationException`**, etc. to your **localization keys** on the client. Exception `Message` is diagnostic (English); prefer **`Reason` / enums** for stable localization IDs.
- `NameValidatorConfig` bans words remain **data** supplied by your game — keep them locale-appropriate if needed.

---

## Threading / PlayerPrefs

Unity **`PlayerPrefs`** (and most `UnityEngine.*` APIs) must run on the **main thread**. If you see:

`Constructors and field initializers will be executed from the loading thread... PlayerPrefs.TrySetInt`

then something called `PlayerPrefs` from a **background thread** or during **scene load** before the main thread is ready.

This SDK **does not use `ConfigureAwait(false)`** on async paths that touch `PlayerPrefs` or other Unity APIs — continuations stay on Unity’s synchronization context. If you fork the SDK, avoid `ConfigureAwait(false)` around awaits whose continuation writes to `PlayerPrefs` or creates MonoBehaviour-owned state.

Also avoid constructing services that synchronously read `PlayerPrefs` in **static field initializers** or **MonoBehaviour field initializers**; create them from **`Start` / `Awake`** or after `await` from a clear main-thread entry point (e.g. your bootstrap `Start`).

---

## Optional next steps

- Central **ILog** abstraction if you integrate Sentry / custom log sinks.
- Strongly typed leaderboard “no row” detection when Unity documents stable exception types.
- Optional split package for Apple Sign In native bridge.
