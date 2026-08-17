# Core Logging

## AppLog

Thin logging façade. Does **not** open UI or report service faults — call sites stay explicit.

Format: `[Tag] message`. Info+ may include `t+12.3s` and `pid=…` when enabled.

### Environment defaults

| Define | MinLevel | Session time | Player id |
|--------|----------|--------------|-----------|
| `UGS_ENV_PRODUCTION` | Warning | off | off |
| `UGS_ENV_STAGING` | Info | off | on |
| `UGS_ENV_DEVELOPMENT` / unset | Verbose | on | on |

Call `AppLog.ConfigureFromEnvironment()` once during bootstrap.

## AppDiagnostics

Writes user metadata to `CrashReportHandler`:

- `env` — production / staging / development
- `build` — from `IBuildInfoProvider`
- `ugs_player_id`
- `fault_top`, `faults` — optional, from `FaultSnapshotProvider`

Configure once: `AppDiagnostics.Configure()`.
