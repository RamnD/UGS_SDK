# Changelog

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
