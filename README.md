# RamnD Unity SDK Monorepo

Unity UPM monorepo for RamnD foundation packages and the Game Services SDK.

All packages share repository version **2.0.0**.

## Packages

| Package | UPM id | Description |
|---------|--------|-------------|
| [Game Services SDK](packages/gameservices-sdk/README.md) | `com.ramnd.gameservices-sdk` | Auth, Economy, IAP, Cloud Save, Ads, Analytics, Remote Config |
| [Core Logging](packages/core-logging/README.md) | `com.ramnd.core-logging` | `AppLog`, crash-report metadata |
| [Service Fault](packages/service-fault/README.md) | `com.ramnd.service-fault` | Headless fault pool + UI bridge |

## Installation

Install individual packages via `?path=`:

```json
{
  "dependencies": {
    "com.ramnd.gameservices-sdk": "https://github.com/RamnD/UGS_SDK.git?path=packages/gameservices-sdk#v2.0.0",
    "com.ramnd.core-logging": "https://github.com/RamnD/UGS_SDK.git?path=packages/core-logging#v2.0.0",
    "com.ramnd.service-fault": "https://github.com/RamnD/UGS_SDK.git?path=packages/service-fault#v2.0.0"
  }
}
```

Local development:

```json
"com.ramnd.gameservices-sdk": "file:../UGS_SDK/packages/gameservices-sdk",
"com.ramnd.core-logging": "file:../UGS_SDK/packages/core-logging",
"com.ramnd.service-fault": "file:../UGS_SDK/packages/service-fault"
```

## Migration from 1.x

Before **2.0.0**, the repo root was a single package:

```json
"com.ramnd.gameservices-sdk": "https://github.com/RamnD/UGS_SDK.git#v1.12.5"
```

From **2.0.0**, use the `packages/gameservices-sdk` path (see above).

## Versioning

- One git tag (`v2.0.0`) covers all packages in this repo
- Each package `package.json` uses the same version number

## Changelog

See [CHANGELOG.md](CHANGELOG.md).
