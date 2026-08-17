# RamnD Core Logging

Tagged logging façade for Unity games.

## Features

- Level-gated logs: `Verbose`, `Debug`, `Info`, `Warning`, `Error`
- Environment defaults via `UGS_ENV_*` scripting defines
- Optional session time / player id enrichment on Info+
- `AppDiagnostics` — Unity crash-report metadata (env, build, player id, optional fault snapshot)

## Installation

```json
"com.ramnd.core-logging": "https://github.com/RamnD/UGS_SDK.git?path=packages/core-logging#v2.0.0"
```

## Quick start

```csharp
AppLog.ConfigureFromEnvironment();
AppDiagnostics.Configure();

AppLog.Info("Bootstrap", "Services ready");
AppLog.SetPlayerId(auth.GetPlayerId());
```

### Build label provider

```csharp
AppDiagnostics.BuildInfoProvider = new MyBuildInfoProvider();
```

### Fault snapshot (optional)

Register from a service-fault package or game code:

```csharp
AppDiagnostics.FaultSnapshotProvider = () =>
{
    // return (topFaultId, commaSeparatedDomains) or null
    return ("Network:offline", "Network");
};
```

See [docs/logging.md](docs/logging.md).
