# Changelog

## [2.1.5] - 2026-08-20

### Fixed
- **Ads privacy / InMobi Choice:** `CreateEventHandler` no longer calls `Delegate.CreateDelegate(Type, Action)` (CS1503); handlers are built via Expression trees for both zero- and multi-arg CMP events.

## [2.1.4] - 2026-08-20

### Added
- **Ads privacy / InMobi Choice:** built-in `InMobiChoiceConsentGate` (reflection over `ChoiceCMP`) with auto-registration when the InMobi CMP Unity package is imported.
- **Ads privacy:** `AdsPrivacyOptions.InMobiChoicePCode` for InMobi Choice initialization (portal p-code without `p-` prefix).

### Changed
- **Ads privacy:** when both Google UMP and InMobi Choice are present, InMobi registers last and takes precedence.
- **Docs:** [ads-privacy.md](docs/ads-privacy.md) documents InMobi Choice alongside Google UMP.

## [2.1.3] - 2026-08-18

### Fixed
- **Optional plugins:** Google Play Games, Apple GameKit, and Google UMP compile only when the matching package/assembly is present (`RAMND_HAS_GOOGLE_PLAY_GAMES`, `RAMND_HAS_APPLE_GAMEKIT`, GMA 8.5+). UGS no longer hard-references `Google.Play.Games`.
- **UMP:** `DebugGeography.NotEEA` maps to `Other` on GMA 9.4+.

## [2.1.2] - 2026-08-18

### Fixed
- **Achievements:** pending unlock ids copy with `HashSet.CopyTo` so Core compiles without `System.Linq` (`ToArray`).
- **Ads privacy:** GDPR flag uses `SetGDPRConsent` on LevelPlay 9.5+ and `LevelPlay.SetConsent` on 9.4.x (`SetGDPRConsent` does not exist in 9.4.1).

## [2.1.1] - 2026-08-18

### Changed
- **Logging:** SDK runtime logs go through `AppLog` (`com.ramnd.core-logging`) instead of raw `Debug.Log*`. Production (`UGS_ENV_PRODUCTION`) keeps Info/Verbose off; call `AppLog.ConfigureFromEnvironment()` at game bootstrap.

### Fixed
- **Auth:** `AuthSessionEnsure` compiles without a missing `UnityEngine.Debug` import (CS0103 on 2.1.0 / 2.0.4).

## [2.1.0] - 2026-08-18

### Added
- **Achievements:** `IPlatformAchievementBridge` + `IAchievementPlatformMapper` for best-effort native achievement mirroring without moving unlock logic out of the game.
- **Achievements:** `UGSServicesBuilder.WithPlatformAchievements(...)` and `IGameServices.PlatformAchievements`.
- **Achievements:** built-in Android Google Play Games and iOS Apple Game Center platform bridges with local pending/report cache and reconnect `FlushAsync`.
- **Sync:** `GameServiceId.PlatformAchievements` registered by the UGS builder.

### Changed
- **Docs:** expand `docs/achievements.md` and README with the portable-store + native-bridge model.

## [2.0.4] - 2026-08-18

### Added
- `AuthSessionEnsure` for soft anonymous auth restore when a session unexpectedly drops but the network is still healthy.
- Built-in auth provider bridges: `AppleGameCenterCredentialsProvider`, `AppleSignInIdentityTokenProvider`, and `GooglePlayGamesProfileProvider`.

## [2.0.2] - 2026-08-17

### Added
- **App update:** optional Google Play Immediate in-app update (`AppUpdatePipeline`, assembly `RamnD.GameServices.UGS.GooglePlayAppUpdate`). No iOS UI — Apple has no native equivalent.

## [2.0.1] - 2026-08-17

### Added
- **Economy:** `IInventoryService.LastRefreshResult` (`EconomyRefreshResult`) so games can tell a live GetBalances snapshot from offline / transport cache fallback.

## [1.12.5] - 2026-08-13

### Fixed
- **IAP / Apple consumables:** do not `ConfirmPurchase` on Economy `INVALIDALREADYREDEEMED` unless the transaction was locally marked redeemed or balances actually grew — prevents finishing a new StoreKit purchase when a stale unified App Receipt caused a false already-redeemed (sequential coin packs).
- **IAP / Apple:** force App Receipt refresh before consumable redeem and refuse redeem when the receipt fingerprint still matches the previous SKU redeem.
- **IAP:** persist recent redeemed store transaction ids locally for safe idempotent confirm; clear on `ClearEntitlements`.

## [1.12.4] - 2026-08-11

### Fixed
- **Ads privacy / UMP:** mark `RamnD.GameServices.UGS.GoogleUmp` with `AlwaysLinkAssembly` + `link.xml` so IL2CPP does not strip the optional assembly (device builds were stuck on `NullAdsUmpConsentGate` despite Editor compiling GoogleUmp). Pipeline now logs the active UMP gate type.

## [1.12.3] - 2026-08-11

### Fixed
- **Ads privacy / UMP:** call `ConsentInformation.CanRequestAds()` as a method (GMA 11.x) — property access failed to compile (`CS0428`).

## [1.12.2] - 2026-08-11

### Fixed
- **Ads privacy / UMP:** `RamnD.GameServices.UGS.GoogleUmp` now has its own `versionDefines` for `com.google.ads.mobile`. The define lived only on the parent UGS asmdef, so `defineConstraints` never passed and the real UMP gate never loaded (`NullAdsUmpConsentGate` / “UMP is not available”).

## [1.12.1] - 2026-08-10

### Fixed
- **IAP:** confirmed non-consumable entitlements (e.g. no-ads) are granted only on explicit `RestorePurchases` / `RestorePurchasesAsync`, not on automatic `FetchPurchases` during init / pending drain — so a wiped or new player does not inherit free entitlements from the store account.

### Added
- **IAP:** `IRealMoneyPurchaseService.ClearEntitlements()` / `CloudSaveEntitlementStore.Clear()` to reset the local entitlement cache after account wipe.

## [1.12.0] - 2026-08-07

### Added
- **Auth:** `AuthPlatform.Google` (OpenID id_token), `Facebook`, `OpenIdConnect` for portable cloud identities.
- **Auth:** `AuthPlatformKind` / `AuthIdentityLayer` — classify game services vs cloud / OIDC; map UGS external type ids.
- **Auth:** `IAuthService.UnlinkWithAccountAsync`, `IsIdentityLinked`, `GetLinkedIdentityTypeIds`.
- **Auth:** credential bridges `RequestGoogleIdTokenAsync`, `RequestFacebookAccessTokenAsync`, `RequestOpenIdConnectIdTokenAsync` (+ optional client/app ids).
- **Docs:** [auth-identities.md](docs/auth-identities.md) — layers, markets, cloud-save contract after `SignedIntoExisting`.

### Changed
- **Auth:** Sign in with Apple (`AuthPlatform.Apple`) is no longer iOS-only in the SDK — works on any OS when the game supplies the identity-token bridge (Android SiWA supported).
- **Auth:** `AccountAlreadyLinked` recover path covers Google OpenID, Facebook, and OpenID Connect (same as Apple / GPGS).

## [1.11.8] - 2026-08-07

### Added
- **Ads privacy:** `AdsPrivacyPipeline` (ATT → Google UMP → LevelPlay COPPA/GDPR) under `RamnD.GameServices.Ads.Privacy`.
- **Ads privacy:** optional `RamnD.GameServices.UGS.GoogleUmp` assembly (define `RAMND_HAS_GOOGLE_MOBILE_ADS` when `com.google.ads.mobile` is present).
- **Docs:** [ads-privacy.md](docs/ads-privacy.md) — AdMob App ID via LevelPlay settings, GMA install, child-directed rules.

### Changed
- **Package:** depends on `com.unity.ads.ios-support` for ATT; UGS asmdef references `Unity.Advertisement.IosSupport` behind `RAMND_HAS_IOS_ATT`.

## [1.11.6] - 2026-08-04

### Fixed
- **IAP:** `RestorePurchasesResult` uses mutable setters instead of `init` so Unity / older compiler targets compile without `System.Runtime.CompilerServices.IsExternalInit`.

## [1.11.5] - 2026-08-04

### Added
- **IAP:** `RestorePurchasesAsync` + `RestorePurchasesResult` to await store restore/fetch and report which configured restorable product ids were found among existing confirmed purchases.

### Changed
- **IAP:** legacy `RestorePurchases()` remains as a fire-and-forget wrapper for backward compatibility.

## [1.11.4] - 2026-07-31

### Fixed
- **Auth recover:** empty-orphan check now treats local `economy_cached_balances` / `economy_pending_tx` progress as non-empty, so deferred gold is not wiped by `DeleteAccount` before `SignedIntoExisting` (prefer SignOut).

## [1.11.3] - 2026-07-30

### Fixed
- **IAP:** Economy `INVALID_ALREADY_REDEEMED` (422 “already been redeemed”) is treated as idempotent success — refresh balances, grant entitlements, and `ConfirmPurchase` so Apple/Google stop redelivering the stuck pending order (common after anonymous first-buy / interrupted redeem).
- **IAP:** Economy `INVALID_ANOTHER_PLAYER` confirms the store order without claiming rewards on the current account (e.g. receipt redeemed before anonymous account delete).

## [1.11.2] - 2026-07-29

### Added
- **Economy:** `PurchaseCatalogEntry.CustomDataJson` + `PurchaseCatalogQuery.CustomDataContains` for dashboard Custom Data tags.
- **Economy:** `GameServiceId.PurchaseCatalog` for reconnect refresh registration.
- **Economy:** shared `UGSEconomyConfigurationSync` used by purchase catalog and virtual purchases (single-flight config sync).

### Fixed
- **Purchase catalog:** do not rebuild the cache when a forced sync fails after a prior success (keeps last good snapshot).
- **Purchase catalog:** `GetAll` returns an immutable copy; unsynced `Query`/`GetAll`/`GetVirtual`/`GetRealMoney` return empty instead of throwing.

### Changed
- **Docs:** [purchase-catalog.md](docs/purchase-catalog.md) responsibility split (SDK vs game) and online-shop-gate guidance.

## [1.11.1] - 2026-07-29

### Fixed
- **Unity packaging:** add missing `.meta` for `docs/purchase-catalog.md` and Bootstrap sample scripts so UPM GUIDs stay stable.

## [1.11.0] - 2026-07-29

### Added
- **Economy:** `IEconomyPurchaseCatalog` + `UGSEconomyPurchaseCatalog` — read-only UGS purchase definitions for dynamic shop UI (`RefreshAsync`, `Query`, `GetAll`, `GetVirtual`, `GetRealMoney`, `TryGet`).
- **Economy:** `PurchaseCatalogEntry`, `PurchaseCatalogLine`, `PurchaseCatalogQuery`, and `PurchaseCatalogFiltering` helpers.
- **Mock:** `MockEconomyPurchaseCatalog` for offline / test flows.
- **Docs:** [docs/purchase-catalog.md](docs/purchase-catalog.md).

## [1.10.4] - 2026-07-29

### Added
- **IAP:** `ProcessPendingPurchasesAsync` — fetch + redeem/confirm stuck pending store orders (Apple/Google). Runs automatically at end of `InitializeAsync` and again before each `PurchaseAsync`.
- **IAP:** `RealMoneyPurchaseOutcome` + `LastPurchaseOutcome` / `LastPurchaseGrantedRewards` so games can distinguish cancel / hard fail / indeterminate (timeout, missing receipt, transport) and avoid “no charges” UX after a grant already landed.
- **IAP:** transaction-id dedupe so the same pending order is not redeemed twice when both `OnPurchasesFetched` and `OnPurchasePending` fire.

### Changed
- **IAP:** missing receipt / redeem timeout / transport failures mark the attempt as `Indeterminate` (not a hard “no charges” failure).

## [1.10.3] - 2026-07-29

### Changed
- **Economy:** default Add/Spend path is deferred when the currency mapper allows the operation offline — optimistic local cache + durable pending queue even while online. Pass `syncImmediately: true` to force an online UGS write for a specific transaction.
- **Economy:** add `HasPendingTransactions` and `FlushPendingAsync` on `IInventoryService<TCurrency>` so games can sync at explicit anchors (leave shop, level load, inventory exit) without blocking shop UI.

## [1.10.2] - 2026-07-28

### Changed
- **Docs / XML:** expand IntelliSense on Core public APIs (locator, sync ids, DTOs, exceptions, Remote Config getters, Virtual Purchases, NetworkRequest); fix stale README install pin, ROADMAP (1.10.0 = Virtual Purchases; Cloud Code → **1.11.0**), and bootstrap (threading, NetworkStatus soft breaker, GameServicesSync, remove non-existent `WithProfanityFilter(ProfanityConfig)`).
- **Docs:** add [docs/virtual-purchases.md](docs/virtual-purchases.md).

## [1.10.1] - 2026-07-28

### Fixed
- **Unity packaging:** add missing `.meta` files for `IVirtualPurchaseService` and `UGSVirtualPurchaseService<TCurrency>` so the new SDK scripts keep stable GUIDs in Unity projects.

## [1.10.0] - 2026-07-28

### Added
- **Economy:** `IVirtualPurchaseService` + `UGSVirtualPurchaseService<TCurrency>` for UGS Economy Virtual Purchases (free bundles / soft-currency bundles) with single-flight purchase, lazy config sync, timeout bounds, and optional post-success balance refresh.

## [1.9.8] - 2026-07-27

### Fixed
- **IAP:** isolate Google Play and Apple App Store redeem flows. Google no longer runs Apple receipt poll / `RefreshAppReceipt`, never falls back to Apple store name, and uses Google-only receipt (`json` + `signature`). Apple keeps its App Receipt poll + refresh path.

## [1.9.7] - 2026-07-26

### Added
- **Sync hub:** `GameServicesSync` + `GameServiceId` — register per-service refresh handlers; `RefreshAsync(service: null)` refreshes all (or one). Auto-refresh on `NetworkStatus.IsOnlineChanged(true)` (reconnect).
- Facade builder registers RemoteConfig / Achievements / Analytics refresh handlers.

### Fixed
- **Economy pending flush:** impossible offline **spend** rejected with `UnprocessableTransaction` / 422 is dropped from the queue (continue flush + GetBalances) instead of throwing `PendingTransactionsFlushFailed` and bricking every boot.

## [1.9.6] - 2026-07-25

### Added
- **Network:** `NetworkRequest.WithTimeout` (default 10s, Auth 30s) so UGS awaits cannot hang on poor mobile / DPI links; abandoned tasks are observed to avoid unobserved exceptions.
- **Network:** soft circuit breaker on `NetworkStatus` — failures inside a 60s window; after 3 trips soft-offline with escalating cooldown (20→40→80s); `ReportSuccess` clears; `ForceOffline` setter publishes changes; `NotifyApplicationResumed` clears cooldown.
- **Network:** `NetworkStatusDriver` (RuntimeInitializeOnLoad) ticks `IsOnlineChanged` and clears soft-offline on app resume.
- **Economy:** pending queue `unconfirmed` status for timed-out / crash-abandoned in-flight writes; `ResolveUnconfirmed` after GetBalances; `ApplyPendingOnTop` after server sync.
- **Achievements:** PlayerPrefs local cache (`achievements_local_cache_v1`) so offline progress survives app kill; single-flight load/flush.

### Fixed
- **Economy / Items / Consumables:** timeout on **writes** is *indeterminate* — reconcile against absolute server balances before queue/refund (no blind double-apply / free-item refund).
- **Items:** grant failure uses confirm-ownership-before-refund; `ReportFailure` runs after compensation.
- **CloudSave:** upload from snapshot; revision-aware `LocalTimestamp`; timed-out push recorded as `_unconfirmedPushTs` so next load does not treat own write as conflict.
- **Auth:** SignIn / Link / UpdatePlayerName / empty-check bounded by timeout; transport failures feed the breaker.
- **IAP:** Economy redeem bounded (15s); timeout does not confirm the store purchase.
- **Leaderboards:** offline short-circuit + timeouts + breaker reporting.
- **Ads (LevelPlay):** rewarded show fails fast when offline / soft-offline.
- **Remote Config:** single-flight fetch; transport detection walks `InnerException`.
- **Analytics:** drains queue on `IsOnlineChanged(true)`; refreshes player id when draining.

### Changed
- `EconomyErrorClassifier` distinguishes `Recoverable` vs `Indeterminate` (`TimeoutException` / UGS `RequestTimeOut` / `NetworkError`).

## [1.9.5] - 2026-07-25

### Fixed
- **IAP (Apple):** harden App Store receipt resolve for Economy redeem.

## [1.9.4] - 2026-07-24

### Fixed
- **Auth recover:** empty-check before SignOut timed out after 8s (Cloud Save / Economy could stall recover); clearer step logs; reuse Link credentials for recover SignIn (GPGS / Apple / Game Center).
- **Auth recover:** if platform SignIn fails after SignOut, fall back to anonymous SignIn so the client is not stuck `NotReady` / unsigned.
- **IAP:** product fetch failure clears the in-flight latch so a later `EnsureProductsFetched` / purchase can retry; missing store product kicks a refetch instead of leaving fetch stuck.

### Added
- **IAP:** `IRealMoneyPurchaseService.EnsureProductsFetched()` for resume / offline→online recovery when `AreProductsReady` is still false.

## [1.9.3] - 2026-07-24

### Fixed
- **Achievements (M1):** `ClearLocalCache()` on `IAchievementService` / UGS / Mock so account switch cannot leak in-memory progress.
- **Items / Consumables (M2):** single-flight mutations — overlapping `TryPurchaseAsync` / `TryConsumeAsync` / `TryGrantAsync` (same item) rejected while in flight.
- **Economy (L1):** pending flush saves cache on `OperationCanceledException` after reverting in-flight rows.
- **Docs (L2):** `docs/economy.md` ItemMapper example uses `GetCost` / `GetCostCurrency` (not `GetPrice`).

### Added
- [docs/consumables.md](docs/consumables.md) + `MockConsumableItemService<TItem>` (L3).

### Changed
- Verbose Economy / Consumables `Debug.Log` gated to Editor / Development builds (warnings/errors unchanged) (L5).
- [docs/auth.md](docs/auth.md) wipe checklist includes Achievements.

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
