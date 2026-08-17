# RamnD Service Fault

Headless pipeline for “service unavailable / operation failed” without raw exception toasts.

## Flow

```text
Reporter → ServiceFaultPool.Report()
         → OnPoolChanged
         → ServiceFaultPopupBridge
         → (game UI presenter)
```

## Installation

```json
"com.ramnd.core-logging": "https://github.com/RamnD/UGS_SDK.git?path=packages/core-logging#v2.0.0",
"com.ramnd.service-fault": "https://github.com/RamnD/UGS_SDK.git?path=packages/service-fault#v2.0.0",
"com.ramnd.gameservices-sdk": "https://github.com/RamnD/UGS_SDK.git?path=packages/gameservices-sdk#v2.0.0"
```

## Quick start

```csharp
ServiceFaultRuntime.EnsureStarted(myCatalog);

ServiceFaultPool.Report(ServiceFaultDomain.CloudSave, ServiceFaultKeys.CloudSaveFailed);
```

UI lives in the game: subscribe to `ServiceFaultPopupBridge.OnFaultChanged` and present your own popup.

See [docs/service-fault.md](docs/service-fault.md).
