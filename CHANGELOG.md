# Changelog

## [1.9.2] - 2026-07-24

### Fixed
- **Economy (C1):** flush sends amount re-read at `TryMarkInFlight` (not the pre-flush snapshot) so mid-flush coalesced deltas are not deleted unread.
- **Consumables (C2):** single-flight `RefreshAsync`; pending grants use `id` + `pending → in_flight` with re-read amount (no double-grant on concurrent refresh).
- **Auth (C3):** orphan empty-check also requires zero Economy balances and empty Player Inventory (not Cloud Save alone).
- **Ads (H-B):** `OnRewardEarned` checks show generation + pending callbacks; generation bumps on session reset.
- **Analytics (H-C):** `CachedAnalyticsSystem` uses `LogEventOrThrow` so failed sends can fall back to the offline queue.
- **Items (M-D):** single-flight `RefreshAsync`.

### Added
- `ClearLocalCache()` on `IInventoryService`, `ICloudSaveService`, `IConsumableItemService` (account delete/switch).

### Changed
- [docs/auth.md](docs/auth.md) documents Economy/Inventory empty-check and `ClearLocalCache` wipe guidance.

## [1.9.1] - 2026-07-24

### Fixed
- **IAP:** `PurchaseAsync` is single-flight (reject while any purchase is in flight) so `LastPurchaseWasUserCancelled` cannot race across overlapping products.
- **IAP:** set `LastPurchaseWasUserCancelled` only when completing an in-flight `PurchaseAsync` (ignore late store callbacks after token cancel).

### Changed
- [docs/iap.md](docs/iap.md) documents cancel-vs-failure and single-flight purchase behaviour.
- Changelog hygiene: `LastPurchaseWasUserCancelled` listed under **1.8.9** (where the code shipped), not 1.8.8.

## [1.9.0] - 2026-07-24

### Changed
- Open **1.9.0** as the prep minor after the closed **1.8.7–1.8.11** correctness stream (no Cloud Code modules in this release).
- [docs/ROADMAP.md](docs/ROADMAP.md): server-authoritative mutations / Cloud Code moved to **1.10.0**; subsequent epics shifted (+1).

## [1.8.11] - 2026-07-24

### Fixed
- **Auth (M10):** NFKC normalize + Cyrillic/Greek lookalike fold for ban-list / pattern checks; names stored NFKC-normalized.
- **Auth (L):** stop logging player display names (PII).
- **CloudSave (L):** corrupt JSON no longer returns `default` (throws); new `TryGet` for missing vs present; payload logs are key/size only (values only in Editor/Dev builds).
- **Economy (L):** `UpdateFromServer` updates only currencies present in the response (no zeroing of missing ids).
- **IAP (L):** after Economy/entitlement grant, store `ConfirmPurchase` failure still completes purchase as success.
- **Achievements (L):** deep-copy state snapshot before Cloud Save serialize.

### Added
- `ICloudSaveService.TryGet<TValue>` — false when missing; throws on corrupt data.

## [1.8.10] - 2026-07-24

### Fixed
- **Bootstrap (M2):** shared `UGSUnityServicesInitializer` — Auth `SignInAsync` and `BuildAsync` both init Unity Services with the resolved environment (no silent prod when calling Auth alone).
- **Bootstrap (M7):** `IGameServices.IsAuthenticated` is live (`Auth?.IsSignedIn`), not a constructor snapshot.
- **Analytics (M5):** event number serialize/parse uses `InvariantCulture`.
- **Analytics (M9):** pending queue locked; batch `DequeueBatch` / `RequeueFront`; drain persists once per batch.
- **Auth (M6):** GPGS TCS uses `RunContinuationsAsynchronously` + cancellation registration (no hang on cancel).
- **IAP (M3):** `ToMinorUnits` uses ISO 4217 fraction digits (JPY/KRW/BHD-safe).
- **CloudSave (M11):** `Set` rejects mapper keys equal to reserved `__ts`.
- **Ads:** `TestAdsManager` only simulates rewards in Editor / Development builds.
- **Auth:** profanity `Regex` rebuilt with a short `MatchTimeout` when callers omit one.
- **Leaderboard:** prefer `RequestFailedException.ErrorCode == 404` over message substring.

## [1.8.9] - 2026-07-24

### Fixed
- **Consumables (H4):** durable pending grant queue; refresh flushes then reapplies unflushed deltas after server rebuild so offline grants are not wiped.
- **CloudSave (H5):** exact `__ts` / dirty comparison (no ±1s tolerance); single-flight `LoadAsync` / `PushToCloudAsync`.
- **IAP (H2):** restore entitlements only from `ConfirmedOrders` (ignore pending deferred payments).
- **Items/Consumables (M8):** PlayerPrefs cache keys namespaced by `typeof(TItem).Name` (with legacy migration).

### Added
- **IAP:** `IRealMoneyPurchaseService.LastPurchaseWasUserCancelled` so games can skip error UI on store-sheet cancel.

### Changed
- [docs/cloud-save.md](docs/cloud-save.md), [docs/iap.md](docs/iap.md) updated for exact versioning and confirmed-order restore.

## [1.8.8] - 2026-07-24

### Fixed
- **Economy (C2/H8):** single-flight `RefreshBalancesAsync` / pending `FlushAsync`; enqueue and flush serialize with re-read-before-persist so mid-flush credits are not dropped.
- **Economy queue:** durable row `id` + `pending → in_flight` status (persist in-flight before UGS call; remove on success; revert on recoverable failure).
- **Economy (M4):** `EconomyErrorClassifier` uses typed `EconomyExceptionReason` (no `"http 5"` substring matching).

### Changed
- [docs/economy.md](docs/economy.md) documents queue lifecycle and single-flight behaviour.

## [1.8.7] - 2026-07-24

### Fixed
- **Achievements (C1):** `_isLoaded` only after successful path; failed warmup no longer leaves an empty cache that can wipe Cloud Save on flush. In-flight load Task; flush requires a cloud baseline and merges local overlay after offline edits.
- **Auth link recover (C3):** on `AccountAlreadyLinked`, delete current player only when Cloud Save is empty; otherwise `SignOut` so non-empty anonymous server data is not destroyed before `SignedIntoExisting`.
- **Items purchase (H3):** no cancel-check after inventory grant; `OperationCanceledException` confirms ownership before refund; refunds use `CancellationToken.None`.
- **LevelPlay ads (H6/H7):** reject overlapping rewarded/interstitial shows with `onFailed`; `_rewardEarned` + late-reward grace so close-before-reward adapters still grant.

### Changed
- [docs/auth.md](docs/auth.md) / [docs/achievements.md](docs/achievements.md) updated for recover + baseline flush behaviour.
- Docs layout: [bug-reports/](docs/bug-reports/README.md) + [ROADMAP.md](docs/ROADMAP.md) (replaces `docs/audit`).

## [1.8.6] - 2026-07-22

### Added
- `IItemService.ClearLocalCache` — wipe in-memory + PlayerPrefs ownership cache (account switch / delete).
- `UGSItemService` loads the last ownership cache from PlayerPrefs in the constructor for instant `IsOwned` reads.

## [1.8.5] - 2026-07-22

### Fixed
- IAP: separate Economy `ProductId` from store SKU via optional `StoreProductId` on `RealMoneyProductDefinition`.
- Unity IAP fetch/purchase/restore use the store SKU; Economy redeem uses the Economy Real Money Purchase id.
- Fixes Apple/Google catalogs when store ids differ in case/format from Economy ids (e.g. `ad_block_forever` vs `AD_BLOCK_FOREVER`).

### Changed
- [docs/iap.md](docs/iap.md) documents `StoreProductId` and the Economy vs store id split.

## [1.8.4] - 2026-07-22

### Added
- `RealMoneyProductInfo` — store-localized product metadata for UI (price string, title, currency).
- `IRealMoneyPurchaseService.TryGetProductInfo` / `AreProductsReady` / `ProductsUpdated` — expose Apple/Google prices after Unity IAP product fetch.
- [docs/iap.md](docs/iap.md) section on populating buy-button labels from store metadata.

## [1.8.3] - 2026-07-21

### Changed
- On `AccountAlreadyLinked` recover: **delete** the current (orphan anonymous) UGS player before `SignInWith*`, instead of `SignOut` only — avoids empty abandoned Unity player IDs. Local game saves are untouched; caller resolves SaveConflict in UI. Falls back to SignOut if delete fails.

## [1.8.2] - 2026-07-21

### Added
- `IAuthService.DeleteAccountAsync` — permanent UGS Authentication account deletion (`DeleteAccountAsync`), clears saved auth method. Required for App Store Guideline 5.1.1; wipe Cloud Save / Economy in the game before calling.

## [1.8.1] - 2026-07-21

### Added
- `AccountLinkResult` for `IAuthService.LinkWithAccountAsync`.
- On `AuthenticationErrorCodes.AccountAlreadyLinked`, SDK signs out and `SignInWith*` the existing player (`SignedIntoExisting`) — reinstall recover. Does not use ForceLink.

### Changed
- `LinkWithAccountAsync` return type: `Task<bool>` → `Task<AccountLinkResult>`.

## [1.8.0] - 2026-07-21

### Added
- Cloud Save optimistic concurrency via `BaseTimestamp` (parent cloud `__ts` after last successful sync).
- `PushToCloudAsync` now returns `SaveConflict?` — detects when another client wrote since `BaseTimestamp` and does not overwrite.
- `SaveConflictSource` (`Load` / `Push`) on `SaveConflict`.
- Auto-apply cloud on `LoadAsync` when local has no unsynced edits and cloud moved ahead.
- `KeepLocal` acknowledges the conflicting cloud version so the next push can overwrite.

### Changed
- `ICloudSaveService.PushToCloudAsync` signature: `Task` → `Task<SaveConflict?>` (awaiters that ignore the result stay source-compatible).
- Conflict docs: report via **return values** (await Load/Push → UI → Apply/Keep), not C# events.
- [docs/cloud-save.md](docs/cloud-save.md) updated for `BaseTimestamp` and push-time conflicts.

## [1.7.1] - 2026-07-21

### Fixed
- Reference `Unity.Purchasing` from Core and UGS asmdefs so IAP types compile in consuming projects.

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
