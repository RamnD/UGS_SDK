# RamnD Unity SDK Monorepo

Unity UPM monorepo for RamnD foundation packages and the Game Services SDK.

`com.ramnd.gameservices-sdk` is **2.6.1**. The other packages remain **2.0.4**.

## Packages

| Package | UPM id | Description |
|---------|--------|-------------|
| [Game Services SDK](packages/gameservices-sdk/README.md) | `com.ramnd.gameservices-sdk` | Auth, Economy, IAP, Cloud Save, Ads, Analytics, Remote Config |
| [Core Logging](packages/core-logging/README.md) | `com.ramnd.core-logging` | `AppLog`, crash-report metadata |
| [Service Fault](packages/service-fault/README.md) | `com.ramnd.service-fault` | Headless fault pool + UI bridge |
| [GameServices ServiceFault Bridge](packages/gameservices-servicefault/README.md) | `com.ramnd.gameservices-servicefault` | Headless `gameservices-sdk` -> `service-fault` adapter |

## Installation

Install individual packages via `?path=`:

```json
{
  "dependencies": {
    "com.ramnd.gameservices-sdk": "https://github.com/RamnD/UGS_SDK.git?path=packages/gameservices-sdk#v2.6.1",
    "com.ramnd.core-logging": "https://github.com/RamnD/UGS_SDK.git?path=packages/core-logging#v2.0.4",
    "com.ramnd.service-fault": "https://github.com/RamnD/UGS_SDK.git?path=packages/service-fault#v2.0.4",
    "com.ramnd.gameservices-servicefault": "https://github.com/RamnD/UGS_SDK.git?path=packages/gameservices-servicefault#v2.0.4"
  }
}
```

Local development:

```json
"com.ramnd.gameservices-sdk": "file:../UGS_SDK/packages/gameservices-sdk",
"com.ramnd.core-logging": "file:../UGS_SDK/packages/core-logging",
"com.ramnd.service-fault": "file:../UGS_SDK/packages/service-fault",
"com.ramnd.gameservices-servicefault": "file:../UGS_SDK/packages/gameservices-servicefault"
```

## Migration from 1.x

Before **2.0.0**, the repo root was a single package:

```json
"com.ramnd.gameservices-sdk": "https://github.com/RamnD/UGS_SDK.git#v1.12.5"
```

From **2.0.0+**, use the `packages/gameservices-sdk` path (see above).

## Versioning

Package versions are independent. `com.ramnd.gameservices-sdk` is **2.6.1**. The other three packages are **2.0.4**. A release tag names the repository snapshot for the package that changed. It does not move every `package.json`.

Install each package at the tag in the manifest block above. Planned SDK minors are in [packages/gameservices-sdk/docs/ROADMAP.md](packages/gameservices-sdk/docs/ROADMAP.md). The only open epic is **2.7.0**. Do not reuse a 1.x number for it: changelog 1.12.0 already shipped as auth.

## Changelog

See [CHANGELOG.md](CHANGELOG.md).
