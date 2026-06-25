# Bootstrap & Initialization

← [Back to README](../README.md)

---

## Overview

All services are created once at startup via `UGSServicesBuilder` (production) or `MockGameServices.CreateDefault()` (editor / tests). Both paths register the result in `GameServicesLocator`.

Generic services (`IInventoryService<T>`, `IItemService<T>`, `ICloudSaveService<TKey>`) live **outside** the façade — create them in the `OnAuthenticated` callback and store them in your own game-side bootstrap.

---

## UGSServicesBuilder — full example

```csharp
// ServicesBootstrap.cs (MonoBehaviour)
[SerializeField] private ProfanityConfig _profanityConfig;
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
        .WithAds(new LevelPlayAdsManager("YOUR_APP_KEY"))
        // ── Post-auth hook: create project-specific services ──────────
        .OnAuthenticated(async auth =>
        {
            _economy   = new UGSEconomyService<CurrencyType>(new CurrencyMapper());
            _cloudSave = new UGSCloudSaveService<SaveKey>(new SaveKeyMapper());

            await _economy.RefreshBalancesAsync();
            var conflict = await _cloudSave.LoadAsync();
            if (conflict.HasValue)
                _cloudSave.ApplyCloud(); // or show conflict UI
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
| `WithAuthProviderCredentials(cfg)` | OAuth IDs for Google Play / Apple |
| `WithNameValidator(NameValidatorConfig)` | Full validator config (words + regex). Overrides `WithProfanityFilter`. |
| `WithProfanityFilter(string[])` | Banned words list only |
| `WithProfanityFilter(Regex)` | Banned pattern only |
| `WithProfanityFilter(ProfanityConfig)` | ScriptableObject (Inspector-editable) |
| `WithAds(IAdsManager)` | Ads manager (LevelPlay, Unity Ads, TestAds…) |
| `OnAuthenticated(Func<IAuthService, Task>)` | Callback after successful sign-in |
| `BuildAsync(CancellationToken)` | Initializes UGS, signs in, runs callback, sets locator |

---

## Mock (editor / offline tests)

```csharp
var services = MockGameServices.CreateDefault();
// Auth is already signed in. Analytics, Ads, Leaderboards are no-op mocks.

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
}

// Direct access (null until BuildAsync completes):
GameServicesLocator.Services?.Auth.GetPlayerName();
```

---

## NetworkStatus (offline testing)

```csharp
NetworkStatus.ForceOffline = true;   // simulate no network in editor
bool online = NetworkStatus.IsOnline; // true if Application.internetReachability != NotReachable AND !ForceOffline
```

---

## Threading rules

`BuildAsync` uses `ConfigureAwait(false)` so its internal continuations run on the **threadpool**. The `OnAuthenticated` callback is called from that threadpool context.

**Rules inside `OnAuthenticated`:**
- ✅ `await SomeUgsApiAsync()` — fine
- ✅ `new UGSCloudSaveService(mapper)` — fine (constructor is safe; PlayerPrefs are loaded lazily)
- ❌ `PlayerPrefs.GetString(...)` — crashes; only safe on main thread
- ❌ `GetComponent<T>()` / `Instantiate` / `FindObjectOfType` — crashes

If you need to touch Unity APIs in `OnAuthenticated`, marshal back with:
```csharp
.OnAuthenticated(async auth =>
{
    await UniTask.SwitchToMainThread(); // if using UniTask
    // or
    await Task.Yield();                 // returns to Unity sync context
    PlayerPrefs.GetString("key");       // now safe
})
```
