# Roadmap — RamnD Game Services SDK

Versioned **product epics**. Each epic ships as a **minor** release: `1.N.0`.

| Stream | Where | Versioning |
|--------|--------|------------|
| Hotfixes / correctness bugs | [bug-reports/](./bug-reports/README.md) | Patch on current minor |
| Planned features / architecture | **this file** | Minor `1.N.0` |

Current package: **2.0.4** (monorepo under `packages/`). Shipped **1.11.0** (Economy Purchase Catalog) + **1.11.1–1.11.2** hardening. Next planned server epic is **1.12.0** Cloud Code — now continued on the **2.x** monorepo line.

---

## 2.x shared-foundation waves

These are extraction waves for reusable headless layers that sit above the low-level SDK but below game-specific UI / progression.

### Wave 1 — auth helpers + service-fault bridge

**Status:** in progress in `2.0.4`

Scope:
- `AuthSessionEnsure`
- built-in platform auth bridges:
  - `AppleGameCenterCredentialsProvider`
  - `AppleSignInIdentityTokenProvider`
  - `GooglePlayGamesProfileProvider`
- `com.ramnd.gameservices-servicefault` with `ServiceFaultInventoryReporter`

### Wave 2 — reset / clear orchestration

**Goal:** extract a clean, headless account reset pipeline from game code.

Planned scope:
- generic core based on Maze `ProgressClearCoordinator`
- signed-in wipe ordering for Cloud Save / Economy / local caches
- callback/hooks surface for game-specific reset steps

Out of scope:
- game UI confirmation flows
- progression-specific local wipe rules

### Wave 3 — conflict + account-link orchestration

**Goal:** extract reusable recover/link plumbing without dragging project UI and profile rules into the SDK.

Planned scope:
- headless conflict coordinator core derived from Maze `SaveConflictCoordinator`
- generic account-link recovery orchestration derived from Maze `ProfilePlatformAuthService`
- clear game callback boundaries for:
  - conflict choice UI
  - local profile merge/import
  - post-recover refresh hooks

Out of scope:
- game-specific profile naming/avatar policies
- project-specific save merge heuristics

---

## [1.9.0] — Prep minor (post–1.8.x) — **shipped**

**Why:** Open the next minor cleanly after the large 1.8.x correctness stream.

### Scope (this release)
- Version bump `1.8.11` → `1.9.0`
- Docs cross-links updated
- Baseline for `1.9.x` patches

### Out of scope for 1.9.0
- Cloud Code modules → **1.12.0**
- Server-verified entitlements → **1.13.0**

---

## [1.9.1–1.9.8] — Patches — **shipped**

Correctness / UX patches on the 1.9 line (IAP single-flight, auth recover, soft circuit breaker, `GameServicesSync`, platform-isolated IAP redeem, …). See [CHANGELOG.md](../CHANGELOG.md).

---

## [1.10.0] — Economy Virtual Purchases — **shipped**

**Why:** Games need store-independent soft-currency / free bundles without going through Unity IAP.

### Scope
- `IVirtualPurchaseService` + `UGSVirtualPurchaseService<TCurrency>`
- Single-flight purchase, lazy Economy config sync, timeouts, optional balance refresh
- Docs: [virtual-purchases.md](./virtual-purchases.md)

### Follow-up patches
- **1.10.1** — Unity `.meta` GUIDs for the new scripts
- **1.10.2** — XML IntelliSense + docs sync (virtual purchases guide, ROADMAP/bootstrap)
- **1.10.3** — deferred economy sync (`FlushPendingAsync`, optimistic online writes)
- **1.10.4** — IAP pending-order drain + indeterminate purchase outcome

---

## [1.11.0] — Economy Purchase Catalog — **shipped**

**Why:** Dynamic shop UI needs UGS-backed purchase definitions (costs, rewards, store ids) without client updates.

### Scope
- `IEconomyPurchaseCatalog` + `UGSEconomyPurchaseCatalog` + `MockEconomyPurchaseCatalog`
- `RefreshAsync`, `Query(PurchaseCatalogQuery)`, `TryGet`, `GetVirtual` / `GetRealMoney`
- Docs: [purchase-catalog.md](./purchase-catalog.md)

### Out of scope for 1.11.0
- Localized RMP prices (still Unity IAP)
- Purchase execution (unchanged VP / IAP services)

### Follow-up patches
- **1.11.1** — missing `.meta` for purchase-catalog doc + Bootstrap sample
- **1.11.2** — failed-sync cache safety, CustomData, shared config sync, `GameServiceId.PurchaseCatalog`

---

## [1.12.0] — Cloud Code: server-authoritative mutations

**Why:** Client Economy has no server idempotency; true at-most-once grants and safe account merge need a server.

### Scope
- Cloud Code modules for: currency grant/spend with **idempotency key**, IAP post-redeem hooks, achievement merge if needed
- Client SDK: call Cloud Code instead of raw `IncrementBalanceAsync` for sensitive ops (reuse durable row `id` from 1.8.x queue)
- Durable server-side dedupe store (transaction id → applied)
- Docs: trust boundary (what stays client-optimistic vs must be server)

### Out of scope for 1.12.0
- Full anti-cheat / replay protection beyond idempotent writes
- Admin orphan-cleanup UI (see 1.14.0)

### Depends on
- Unity Cloud Code project wiring in consuming games (Maze)
- Stable client queue from 1.8.x / 1.9.x

---

## [1.13.0] — Server-verified entitlements

**Why:** Cloud Save entitlements are client-writable (`RedeemWithEconomy = false` / VIP-style flags).

### Scope
- Validate paid entitlements against store receipts / Economy redeem / Cloud Code
- Local entitlement set = cache only
- Maze: no-ads and similar stay restore-from-store + server confirm

### Depends on
- 1.12.0 Cloud Code (or Economy redeem-only policy enforced in docs + game)

---

## [1.14.0] — Account link: orphan cleanup & merge helpers

**Why:** Client cannot `DeleteAccount` on the previous anonymous player after `SignIntoExisting`. Empty-check + SignOut leaves orphans.

### Scope
- Cloud Code / Admin API: delete orphan `playerId` list reported by clients
- Optional merge helpers: push snapshot metadata, economy reconcile rules
- Auth SDK: report `PreviousPlayerId` on recover for cleanup queue
- Docs: empty → Delete; non-empty → SignOut + conflict (client policy remains)

### Depends on
- 1.12.0
- Maze SaveConflict + post-link IAP restore (game-side, can land earlier)

---

## [1.15.0] — Offline-first façade (LocalReady)

**Why:** Games like Maze should play without blocking forever on UGS bootstrap; ads/IAP hidden offline, pools flush on reconnect.

### Scope
- SDK: clearer `LocalReady` / sync signals (or documented game pattern + optional helpers)
- Single reconnect flush entrypoint (economy pending, cloud push, analytics drain) — builds on `GameServicesSync` (1.9.7)
- `playerId` guard on durable queues when session changes
- Sample / docs for degraded mode

### Note
Maze may ship UX (preloader timeout, hide IAP offline) on game side before this minor; this epic standardizes it in the package.

---

## [1.16.0] — Mutation single-flight API surface

**Why:** Ads/IAP/CloudSave/Auth need one consistent “busy → reject” contract beyond ad-hoc fixes.

### Scope
- Documented single-flight guarantees per service
- Ads: busy reject + reward/close FSM (if any gaps remain after 1.8.x / 1.9.x)
- Optional shared `InFlightGate` helper in Core
- Analytics drain locking + optional client event-id dedupe hooks

### Note
Critical ads/economy races are fixed in **1.8.x** / **1.9.x** hotfixes; this epic is API polish and consistency.

---

## Backlog (unscheduled)

- Multi-device achievement merge (per-id, not full-blob LWW)
- WriteLock-based Economy OCC helpers (still not idempotency)
- Stronger NetworkStatus than `internetReachability` — **partial in 1.9.6+** (timeouts + soft circuit breaker + `GameServicesSync` reconnect hub in 1.9.7); latency probe / offline-first facade still open → 1.15.0
- GDPR wipe completeness checklist automation

---

## Versioning cheat sheet

```
1.8.x   — hotfixes from bug-reports (closed: 1.8.7–1.8.11)
1.9.0   — prep minor after hotfix stream
1.9.x   — network / sync / IAP / auth patches
1.10.0  — Economy Virtual Purchases (shipped)
1.10.1  — Virtual Purchase .meta GUIDs
1.10.2  — XML docs + markdown sync
1.10.3  — deferred economy sync
1.10.4  — IAP pending drain + indeterminate outcome
1.11.0  — Economy Purchase Catalog (shipped)
1.12.0  — Cloud Code idempotent mutations (server) ← next planned
1.13.0  — server-verified entitlements
1.14.0  — orphan cleanup / link merge helpers
1.15.0  — offline-first LocalReady in SDK
1.16.0  — unified single-flight surface
```

When starting an epic, bump `package.json` to `1.N.0`, add CHANGELOG section, and link the epic heading here as **shipped**.
