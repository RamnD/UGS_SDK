# Changelog

## [1.7.0] - 2026-07-21

### Added
- Portable real-money purchase layer: `IRealMoneyPurchaseService`, `RealMoneyProductDefinition`, `CloudSaveEntitlementStore<TKey>`.
- `UGSRealMoneyPurchaseService<TKey, TCurrency>` — Unity IAP store bridge + UGS Economy receipt redeem + optional entitlement persistence via `ICloudSaveService<TKey>`.
- [docs/iap.md](docs/iap.md) — setup guide for consumables, bundles, and non-consumable entitlements (e.g. `no_ads`).
- Unity `.meta` GUIDs for IAP folders, scripts, and docs so Package Manager imports cleanly.

### Changed
- `package.json`: added `com.unity.purchasing` dependency and `iap` keyword.
- README table of contents links to IAP documentation.

## [1.6.10] - 2026-07-17

### Fixed
- Google Play Games Link/SignIn: use `PlayGamesPlatform.Activate()` + `ManuallyAuthenticate` when not already signed in. Previously `Authenticate()` only did a silent check (no UI), so the Profile Link button could appear to do nothing after dismissing the startup GPGS prompt.

## [1.6.9] - 2026-07-16

### Added
- `AuthPlatform.AppleGameCenter` — primary iOS gaming identity (pair to Google Play Games).
- `AppleGameCenterCredentials` + `GameServicesAuthProviderConfig.RequestAppleGameCenterCredentialsAsync`.
- `UGSAuthService` SignIn/Link/Unlink path for Apple Game Center via injected GameKit credentials.
- iOS bootstrap default platform is now `AppleGameCenter` (SIWA `AuthPlatform.Apple` remains available).

### Changed
- Auth docs updated for Game Center as the recommended iOS provider for games; SIWA stays optional.

## [1.6.8] - 2026-07-16

### Added
- `GameServicesAuthProviderConfig.RequestAppleIdentityTokenAsync` — game-supplied Apple identity token (JWT) bridge for SignIn/Link with Apple.
- `UGSAuthService` SignIn/Link with Apple now uses the injected token provider instead of throwing a TODO.

### Changed
- Auth docs/README updated for Apple Sign-In wiring via `RequestAppleIdentityTokenAsync`.

## [1.6.7] - 2026-07-16

### Added
- `LevelPlayAdsManager`: on `UGS_ENV_STAGING` / `UGS_ENV_DEVELOPMENT`, call `SetAdaptersDebug(true)` and `ValidateIntegration()` before init (logs adapter status + Advertising ID for test devices).

## [1.6.6] - 2026-07-16

### Changed
- `UGSEconomyService`: recoverable network failures on Add/Spend now apply optimistic cache and durable pending queue (when mapper allows), instead of hard-failing gameplay.
- `TrySpendCurrencyAsync` no longer throws on network errors — returns `false` or queues locally.
- `PendingTransactionQueue`: per-currency coalescing, key rename `economy_pending_adds` → `economy_pending_tx` (with migration), soft-stop on recoverable flush failures.
- `RefreshBalancesAsync` keeps local cache while pending deltas remain (does not overwrite with server mid-queue).
- Economy docs updated for durable queue / recoverable-failure behaviour.

### Added
- `EconomyErrorClassifier` for recoverable vs hard economy transport failures.

## [1.6.0] - 2026-07-08

### Added
- Portable `IAchievementService` module on `IGameServices` (`Services.Achievements`).
- `UGSAchievementService` backed by UGS Cloud Save with runtime in-memory cache and immediate flush on mutation when online.
- `MockAchievementService` for editor/tests.
- `UGSServicesBuilder.WithAchievements()` opt-in module toggle.
- `docs/achievements.md`.

### Changed
- Package metadata now advertises achievements support.
- Bootstrap docs and README examples now include achievements and environment behavior.

### Fixed
- `UGSEnvironmentResolver` now logs the resolved environment and reports conflicting `UGS_ENV_*` symbol combinations while keeping deterministic priority.

## [1.5.1] - 2026-06-26

### Fixed
- Added missing Unity `.meta` files for Remote Config, pre-auth analytics cache (`CachedAnalyticsSystem`, `PendingAnalyticsQueue`, `AnalyticsEventSerializer`, `PendingAnalyticsRecord`), and `docs/remote-config.md` — proper `uuid4` GUIDs (fixes GUID conflicts after v1.5.0 git install).

## [1.5.0] - 2026-06-26

### Added
- `IRemoteConfigService` on `IGameServices` (`Services.RemoteConfig`).
- `UGSRemoteConfigService` with PlayerPrefs cache (`RemoteConfigCache`) for offline reads.
- `MockRemoteConfigService` for editor / tests.
- `UGSServicesBuilder.WithRemoteConfig()` — fetch after auth.
- `docs/remote-config.md`.

### Changed
- Dependency: `com.unity.remote-config` 4.2.5.

## [1.4.5] - 2026-06-25

### Fixed
- Replaced placeholder `.meta` GUIDs with proper `uuid4` values (fixes GUID conflicts in consuming projects).
- Added missing `Runtime.meta` at package root.

## [1.4.4] - 2026-06-25

### Fixed
- Added missing Unity `.meta` files for `package.json`, `README.md`, `CHANGELOG.md`, `LICENSE`, and `docs/` — removes Package Manager immutable-folder warnings on git install.

## [1.4.3] - 2026-06-25

### Changed
- All XML doc comments, inline comments, and documentation are now English-only.
- Removed `docs/ru/` Russian documentation folder.

## [1.4.2] - 2026-06-25

### Added
- MIT `LICENSE`.
- README section [Security & credentials](README.md#security--credentials): credential ownership, public vs secret values, game `.gitignore` hints, platform auth status, Unity disclaimer.

## [1.4.1] - 2026-06-25

### Fixed
- Cloud Save dependency ID: `com.unity.services.cloudsave` (was invalid `com.unity.services.cloud-save`).

## [1.4.0] - 2026-06-25

### Added
- Standalone UPM package layout (`com.ramnd.gameservices-sdk`).
- `RamnD.GameServices.UGS` assembly definition for UGS runtime.
- Bootstrap sample under `Samples~/Bootstrap`.

### Changed
- Legacy `UnityAdsManager` compiled only when `RAMND_LEGACY_UNITY_ADS` is defined.
- Package dependencies aligned with current UGS / LevelPlay versions used in production projects.

### Removed
- `com.unity.ads` from package dependencies (use LevelPlay; legacy ads behind optional define).
