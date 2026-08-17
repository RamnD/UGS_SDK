# Service Fault pipeline

## Components

| Type | Role |
|------|------|
| `ServiceFaultPool` | Global store, dedupe, sticky/one-shot |
| `ServiceFaultPolicy` | Sticky vs one-shot rules |
| `ServiceFaultPopupBridge` | Forwards pool changes to UI consumers |
| `ServiceFaultRuntime` / `ServiceFaultHost` | DDOL lifecycle |
| `NetworkConnectivityFaultWatcher` | Maps `NetworkStatus` → offline fault |
| `IServiceFaultCatalog` | Game or `DefaultServiceFaultCatalog` resolves title/body/icon |
| `ServiceFaultFallbacks` | English safety-net copy when catalog misses a key |
| `DefaultServiceFaultCatalog` | Optional plain-string ScriptableObject catalog |

## Invariants

- Reporters use stable keys from `ServiceFaultKeys`
- Duplicate id only bumps counter — no `OnPoolChanged` (anti-spam)
- Sticky faults suppress after dismiss until `Clear`
- One-shot faults clear fully on dismiss
- Reconnect clears deferred one-shots via `ClearActiveOnReconnect`

## UI boundary

This package does **not** include popups. Games implement a presenter that:

1. listens to `ServiceFaultPopupBridge.OnFaultChanged`
2. reads `TryGetTopFault`
3. calls `NotifyDismissed(faultId)` when the user closes the popup
