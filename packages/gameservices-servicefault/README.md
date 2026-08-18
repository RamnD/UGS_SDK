# RamnD GameServices ServiceFault Bridge

Headless adapter package that maps common `gameservices-sdk` economy/network failures
into `service-fault` entries.

Use this when you want:

- shared `InventoryOperationException` -> `ServiceFaultPool` reporting
- shared economy refresh outcome reporting
- a reusable bridge between RamnD service/runtime packages without game UI code

This package does not include UI. Pair it with `com.ramnd.service-fault` and a
game-side presenter/catalog.
