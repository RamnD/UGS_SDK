# Changelog

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
