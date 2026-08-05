# Bootstrap & Initialization

← [Back to README](../README.md)

---

## Overview

All services are created once at startup via `UGSServicesBuilder` (production) or `MockGameServices.CreateDefault()` (editor / tests). Both paths register the result in `GameServicesLocator`.

Generic services (`IInventoryService<T>`, `IItemService<T>`, `ICloudSaveService<TKey>`, `IVirtualPurchaseService`, `IRealMoneyPurchaseService`) live **outside** the façade — create them in the `OnAuthenticated` callback and store them in your own game-side bootstrap.

`Achievements` live **inside** the façade because they are portable and non-generic: use `GameServicesLocator.Services?.Achievements`.

---

## UGSServicesBuilder — full example

```csharp
// ServicesBootstrap.cs (MonoBehaviour)
[SerializeField] private ProfanityConfig _profanityConfig; // game-side ScriptableObject
[SerializeField] private bool _forceAnonymous = true;

private IInventoryService<CurrencyType> _economy;
private ICloudSaveService<SaveKey>     _cloudSave;

private async void Start()
{
    var services = await new UGSServicesBuilder()
        // ── Auth options ──────────────────────────────────────────────
        .WithForceAnonymous(_forceAnonymous)            // true = always anonymous (dev)
        .WithAuthProviderCredentials(new GameServicesAuthProviderConfig
        {
            GooglePlayGamesOAuthWebClientId = "YOUR_OAUTH_CLIENT_ID",
            AppleServicesId = "com.yourcompany.yourgame",
        })
        // ── Name validation ───────────────────────────────────────────
        .WithNameValidator(_profanityConfig?.ToValidatorConfig())   // highest priority
        // .WithProfanityFilter("badword1", "badword2")            // alternative: inline list
        // .WithProfanityFilter(new Regex(@"bad\w+"))              // alternative: regex
        // ── Ads ───────────────────────────────────────────────────────
        .WithAds(new LevelPlayAdsManager("YOUR_APP_KEY")) // LevelPlay mediation (Pangle optional)
        // ── Optional portable modules ─────────────────────────────────
        .WithAchievements()
        // ── Post-auth hook: create project-specific services ──────────
        .OnAuthenticated(async auth =>
        {
            _economy   = new UGSEconomyService<CurrencyType>(new CurrencyMapper());
            _cloudSave = new UGSCloudSaveService<SaveKey>(new SaveKeyMapper());

            await _economy.RefreshBalancesAsync();
            var conflict = await _cloudSave.LoadAsync();
            if (conflict.HasValue)
            {
                // Show Local vs Cloud UI, then ApplyCloud() or KeepLocal() (+ Push if keeping local).
                // See docs/cloud-save.md — conflicts are return values, not events.
                _cloudSave.ApplyCloud();
            }

            // Reconnect refresh for typed services (façade ones are registered by the builder):
            GameServicesSync.Register(GameServiceId.Economy, ct => _economy.RefreshBalancesAsync(ct));
            GameServicesSync.Register(GameServiceId.CloudSave, async ct =>
            {
                var c = await _cloudSave.LoadAsync(ct);
                // optional: surface conflict UI
            });
        })
        .BuildAsync(destroyCancellationToken);

    // GameServicesLocator is set inside BuildAsync
    IsReady = true;
}
```

### Builder methods reference

| Method | Description |
|--------|-------------|
| `WithForceAnonymous(bool)` | Skip platform login; always sign in anonymously |
| `WithAuthProviderCredentials(cfg)` | OAuth IDs + optional Apple / Game Center credential bridges |
| `WithNameValidator(NameValidatorConfig)` | Full validator config (words + regex). Overrides `WithProfanityFilter`. Convert game ScriptableObjects via `ToValidatorConfig()` in the consuming project. |
| `WithProfanityFilter(string[])` | Banned words list only |
| `WithProfanityFilter(Regex)` | Banned pattern only |
| `WithAds(IAdsManager)` | Ads manager (LevelPlay, TestAds, optional Unity Ads; Pangle via LevelPlay mediation) |
| `WithCachedAnalytics(bool)` | Disk-backed offline analytics queue |
| `WithRemoteConfig(bool)` | UGS Remote Config fetch after auth + PlayerPrefs cache |
| `WithAchievements(bool)` | Portable achievement module backed by UGS Cloud Save |
| `OnAuthenticated(Func<IAuthService, Task>)` | Callback after successful sign-in |
| `BuildAsync(CancellationToken)` | Initializes UGS, signs in, runs callback, sets locator |

---

## Mock (editor / offline tests)

```csharp
var services = MockGameServices.CreateDefault();
// Auth is already signed in. Analytics, Ads, Leaderboards, RemoteConfig, Achievements are mocks.

var economy   = new MockInventoryService<CurrencyType>();
var cloudSave = new MockCloudSaveService<SaveKey>();
```

Mock services implement the same interfaces — no change to UI or game-logic code.

---

## Accessing services at runtime

```csharp
// Safe nullable access:
if (GameServicesLocator.TryGet(out var svc))
{
    svc.Analytics?.LogEvent(new LevelStartedEvent { Level = 3 });
    svc.Leaderboards?.SubmitScoreAsync("run_leaderboard", score);
    int cap = svc.RemoteConfig?.GetInt("inventory_max_cap", 6) ?? 6;
    bool firstWinUnlocked = svc.Achievements?.TryGetState("first_win", out var state) == true && state.IsUnlocked;
}

// Direct access (null until BuildAsync completes):
GameServicesLocator.Services?.Auth.GetPlayerName();
```

---

## Game Services Sync (reconnect)

`GameServicesSync` is the reconnect refresh hub. The UGS builder registers Remote Config / Achievements / Analytics handlers. **Economy, Items, and Cloud Save** are project-typed — register them from your bootstrap (see example above).

```csharp
// Manual refresh (all registered):
await GameServicesSync.RefreshAsync();

// One service:
await GameServicesSync.RefreshAsync(GameServiceId.Economy);

// Auto: when NetworkStatus.IsOnline flips to true, all registered handlers run.
```

---

## NetworkStatus (offline testing & soft breaker)

```csharp
NetworkStatus.ForceOffline = true;   // simulate no network in editor
bool online = NetworkStatus.IsOnline; // reachability && !ForceOffline && !soft-offline

// Soft circuit breaker (timeouts / DPI / poor link):
// after 3 recoverable failures in 60s → soft-offline cooldown 20→40→80s
NetworkStatus.ReportFailure();
NetworkStatus.ReportSuccess();       // clears soft-offline
NetworkStatus.IsSoftOffline;
NetworkStatus.NotifyApplicationResumed(); // clears cooldown on app resume

// Tick / IsOnlineChanged are driven by NetworkStatusDriver (RuntimeInitializeOnLoad).
```

See CHANGELOG **1.9.6** / **1.9.7** for soft breaker + sync hub details.

---

## Threading rules

`BuildAsync` does **not** use `ConfigureAwait(false)` — continuations stay on Unity’s synchronization context. `OnAuthenticated` is therefore invoked on the main thread in normal Unity play mode.

**Still avoid:**
- Calling `PlayerPrefs` / Unity APIs from **background threads** you spawn yourself
- Constructing services that touch `PlayerPrefs` in **static / field initializers**

If you fork async work off the main thread inside `OnAuthenticated`, marshal back before Unity APIs:

```csharp
.OnAuthenticated(async auth =>
{
    await UniTask.SwitchToMainThread(); // if using UniTask
    // or
    await Task.Yield();                 // returns to Unity sync context when started from main thread
    PlayerPrefs.GetString("key");
})
```

Also see [README — Threading / PlayerPrefs](../README.md#threading--playerprefs).

---

## Environments

`UGSServicesBuilder` resolves the UGS environment name through `UGSEnvironmentResolver`.

Priority:

1. `UGS_ENV_PRODUCTION`
2. `UGS_ENV_STAGING`
3. `UGS_ENV_DEVELOPMENT`
4. fallback: `development`

If more than one `UGS_ENV_*` symbol is defined, the SDK logs an error and still applies the priority list above.
