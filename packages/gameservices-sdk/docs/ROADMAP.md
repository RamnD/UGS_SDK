# Roadmap — RamnD Game Services SDK

Versioned **product epics** for `com.ramnd.gameservices-sdk`. Each epic ships as **one minor** of that package. Foundation packages (`core-logging`, `service-fault`, `gameservices-servicefault`) stay on their own versions until an epic changes them.

| Stream | Where | Versioning |
|--------|--------|------------|
| Hotfixes / correctness bugs | [bug-reports/](./bug-reports/README.md) | Patch on the current SDK minor |
| Planned features / architecture | **this file** | The single **next** minor below |

Package versions come from each `package.json` and from [CHANGELOG.md](../CHANGELOG.md). This file does not invent a second current version.

---

## Current

| Package | Version | Role |
|---------|---------|------|
| `com.ramnd.gameservices-sdk` | **2.6.1** | This roadmap |
| `com.ramnd.core-logging` | **2.0.4** | Unchanged since the 2.0.4 foundation release |
| `com.ramnd.service-fault` | **2.0.4** | Unchanged since the 2.0.4 foundation release |
| `com.ramnd.gameservices-servicefault` | **2.0.4** | Unchanged since the 2.0.4 foundation release |

**Next, and the only epic that may start: [2.7.0](#2.7.0) Cloud Code.** Queued rows are ordered. A row starts only after the previous minor is in the changelog. Nothing else is in progress.

Cloud Code stays next because this plan already named it as the next server epic. The old labels 1.12–1.16 were never released as those epics. Changelog **1.12.0–1.12.5** is a shipped auth / IAP / ads line and is not Cloud Code. Former Wave 2 and Wave 3 are [2.12.0](#2.12.0) and [2.13.0](#2.13.0), after the server and client epics that were already sequenced.

When an epic starts, bump only the affected package to that minor, add a changelog section, and mark the heading here **shipped**. Do not reuse a 1.x number.

---

## [2.7.0] — Cloud Code: server-authoritative mutations — **next**

<a id="2.7.0"></a>

**Status:** not started. This is the only open epic.

**Why:** Client Economy has no server idempotency. At-most-once grants and a safe account merge need a server.

### Scope
- Cloud Code modules for currency grant/spend with an **idempotency key**, IAP post-redeem hooks, and achievement merge if needed
- Client SDK calls Cloud Code instead of raw `IncrementBalanceAsync` for sensitive ops (reuse the durable row `id` from the 1.8.x queue)
- Durable server-side dedupe store (transaction id → applied)
- Docs: trust boundary (what stays client-optimistic vs what must be server)

### Out of scope
- Full anti-cheat / replay protection beyond idempotent writes
- Admin orphan-cleanup UI ([2.9.0](#2.9.0))

### Depends on
- Unity Cloud Code project wiring in the consuming game
- Stable client queue from 1.8.x / 1.9.x

### Done when
- The module applies a currency grant and a spend under an idempotency key and records transaction id → applied
- Sensitive client balance changes go through that module and reuse the durable queue row id
- The trust-boundary doc lists which operations stay client-optimistic
- `com.ramnd.gameservices-sdk` is **2.7.0** and the changelog has this section

---

## Queued

One minor at a time, in this order.

### [2.8.0] — Server-verified entitlements

<a id="2.8.0"></a>

**Status:** not started.

**Why:** Cloud Save entitlements are client-writable (`RedeemWithEconomy = false` / VIP-style flags).

### Scope
- Validate paid entitlements against store receipts, Economy redeem, or Cloud Code
- Local entitlement set = cache only

### Out of scope
- Game-specific product lists. A game such as Maze keeps no-ads as restore-from-store plus the server confirm; that policy is not an SDK type.

### Depends on
- [2.7.0](#2.7.0), or a redeem-only policy enforced in the docs and in the game

### Done when
- A paid entitlement is accepted only after a store receipt, an Economy redeem, or a Cloud Code check
- Docs call the local entitlement set a cache
- The dependency above is satisfied, and the package minor is **2.8.0**

### [2.9.0] — Account link: orphan cleanup and merge helpers

<a id="2.9.0"></a>

**Status:** not started.

**Why:** The client cannot `DeleteAccount` on the previous anonymous player after `SignIntoExisting`. An empty-check plus SignOut leaves orphans.

### Scope
- Cloud Code / Admin API deletes an orphan `playerId` list reported by clients
- Optional merge helpers: push snapshot metadata, economy reconcile rules
- Auth SDK reports `PreviousPlayerId` on recover for the cleanup queue
- Docs: empty → Delete; non-empty → SignOut + conflict (the client policy for the non-empty case stays in the game)

### Depends on
- [2.7.0](#2.7.0)
- The game can ship its own conflict UI and post-link IAP restore before this minor

### Done when
- After `SignIntoExisting`, auth reports the previous player id
- Cloud Code or the Admin API can delete that orphan id
- The empty / non-empty doc rule above is in the auth docs
- The package minor is **2.9.0**

### [2.10.0] — Offline-first façade (LocalReady)

<a id="2.10.0"></a>

**Status:** not started.

**Why:** A game should play without blocking forever on UGS bootstrap. Ads and IAP stay hidden offline. Pools flush on reconnect.

### Scope
- SDK signal for `LocalReady` (or a documented helper with the same meaning)
- One reconnect flush entrypoint for economy pending, cloud push, and analytics drain. Builds on `GameServicesSync` (1.9.7)
- `playerId` guard on durable queues when the session changes
- Sample / docs for degraded mode

### Out of scope
- A game may ship the preloader timeout and hide IAP while offline before this minor. This epic is the package form of that pattern.

### Done when
- Gameplay can proceed from a `LocalReady` signal without waiting out UGS bootstrap
- One reconnect call flushes economy pending, cloud push, and analytics
- A durable queue does not apply rows stamped for a different player id
- The package minor is **2.10.0**

### [2.11.0] — Mutation single-flight API surface

<a id="2.11.0"></a>

**Status:** not started.

**Why:** Ads, IAP, Cloud Save, and Auth need one “busy → reject” contract. The 1.8.x / 1.9.x hotfixes closed the known races; this epic is the shared surface.

### Scope
- Documented single-flight rule per mutating service
- Ads: busy reject and the reward/close sequence, if a gap remains after 1.8.x / 1.9.x
- Optional shared `InFlightGate` helper in Core
- Analytics drain locking and optional client event-id dedupe hooks

### Done when
- Each mutating service documents that a second call while busy is rejected
- Remaining ads gaps are either closed here or listed as still out of scope
- `InFlightGate` is added only if more than one service still needs the same helper
- The package minor is **2.11.0**

### [2.12.0] — Reset / clear orchestration

<a id="2.12.0"></a>

**Status:** not started. Former Wave 2.

**Why:** Account reset is still game code. The package needs a headless pipeline the game can call.

**Extraction source:** Maze `ProgressClearCoordinator`. That name is the game type to read. It is not the SDK type.

### Exit contract
- Public API is a headless reset orchestrator. Its names do not mention a game.
- For a signed-in player the SDK step order is:
  1. Clear local SDK caches (economy pending and entitlements) before any server zero, so a deferred write cannot refill them.
  2. Game callback for project-specific local wipe (progression, inventory, tutorial flags).
  3. Server economy zero only when the same player remains. Skip it when the account is about to be deleted.
  4. Push the empty cloud save. On conflict, keep local and retry. If the push does not confirm, leave a wipe-pending flag so the next load cannot restore the old remote.
- Account deletion stays a separate call after the wipe. Confirmation UI stays in the game. If delete fails, the game signs out.
- Starter grants, offer state, and profile scrubbing stay in the game callback.

### Out of scope
- Game UI
- Progression-specific wipe rules

### Done when
- The orchestrator implements the four SDK steps above and takes the game callback at step 2
- A wipe-pending flag blocks a later load from restoring the pre-wipe remote until a push confirms
- No public type is named after the Maze source
- The package minor is **2.12.0**

### [2.13.0] — Conflict and account-link orchestration

<a id="2.13.0"></a>

**Status:** not started. Former Wave 3.

**Why:** Recover and link still drag project UI and profile rules through the call chain. The package needs the headless part only.

**Extraction source:** Maze `SaveConflictCoordinator` and `ProfilePlatformAuthService`. Those names stay in the game.

### Exit contract
- The SDK stores one pending Cloud Save conflict and exposes a blocking flag. Enqueue does not touch UI.
- The game supplies three callbacks: conflict-choice UI, local profile merge/import, and post-recover refresh.
- The SDK applies Keep local or Apply cloud, then runs the refresh callback.
- While a wipe from [2.12.0](#2.12.0) is pending, Apply cloud is not offered.
- Link recovery that ends in `SignedIntoExisting` hands off to this conflict path. Which identity button exists, child-directed link policy, naming, and avatars stay in the game.

### Out of scope
- Profile naming and avatar policy
- Project-specific save merge heuristics
- Rebuilding the platform auth bridges. Those shipped in 2.0.4.

### Depends on
- [2.12.0](#2.12.0) for the wipe-pending rule. The conflict queue itself can be designed earlier and must not ship ahead of its minor.

### Done when
- A conflict can be enqueued off the main thread and resolved only through the game callbacks
- Wipe-pending forces keep-local
- `SignedIntoExisting` is documented as the handoff into this path
- No public type is named after the Maze sources
- The package minor is **2.13.0**

---

## Shipped

### Foundation wave — auth helpers and service-fault bridge — **shipped in 2.0.4**

Former Wave 1. Closed in [2.0.4](../CHANGELOG.md). Later 2.x patches fixed compile and call-site bugs; they did not reopen the wave.

- `AuthSessionEnsure`
- `AppleGameCenterCredentialsProvider`
- `AppleSignInIdentityTokenProvider`
- `GooglePlayGamesProfileProvider`
- `com.ramnd.gameservices-servicefault` with `ServiceFaultInventoryReporter`

### [1.9.0] — Prep minor (post–1.8.x) — **shipped**

**Why:** Open the next minor cleanly after the 1.8.x correctness stream.

### Scope
- Version bump `1.8.11` → `1.9.0`
- Docs cross-links updated
- Baseline for `1.9.x` patches

### Out of scope for 1.9.0
- Cloud Code modules → [2.7.0](#2.7.0)
- Server-verified entitlements → [2.8.0](#2.8.0)

### [1.9.1–1.9.8] — Patches — **shipped**

Correctness / UX patches on the 1.9 line (IAP single-flight, auth recover, soft circuit breaker, `GameServicesSync`, platform-isolated IAP redeem). See [CHANGELOG.md](../CHANGELOG.md).

### [1.10.0] — Economy Virtual Purchases — **shipped**

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

This number is Virtual Purchases. It is not Cloud Code.

### [1.11.0] — Economy Purchase Catalog — **shipped**

**Why:** Dynamic shop UI needs UGS-backed purchase definitions (costs, rewards, store ids) without client updates.

### Scope
- `IEconomyPurchaseCatalog` + `UGSEconomyPurchaseCatalog` + `MockEconomyPurchaseCatalog`
- `RefreshAsync`, `Query(PurchaseCatalogQuery)`, `TryGet`, `GetVirtual` / `GetRealMoney`
- Docs: [purchase-catalog.md](./purchase-catalog.md)

### Out of scope for 1.11.0
- Localized real-money prices (still Unity IAP)
- Purchase execution (unchanged virtual-purchase / IAP services)

### Follow-up patches
- **1.11.1** — missing `.meta` for the purchase-catalog doc and the Bootstrap sample
- **1.11.2** — failed-sync cache safety, CustomData, shared config sync, `GameServiceId.PurchaseCatalog`

This number is the purchase catalog. Server entitlement checks are [2.8.0](#2.8.0).

### [1.12.0–1.12.5] — Auth identities, IAP restore, ads privacy — **shipped**

See [CHANGELOG.md](../CHANGELOG.md). **1.12.0** added Google OpenID, Facebook, and OpenID Connect, plus unlink and identity-layer helpers. **1.12.1–1.12.5** are IAP entitlement and ads-privacy patches. None of them is the Cloud Code epic.

### [2.0.0–2.6.1] — Monorepo line — **shipped**

**2.0.0** moved the repo to `packages/`. **2.0.4** shipped the foundation wave above. **2.1.x–2.6.1** are changelog patches and minors (logging, LevelPlay, analytics, dataset names, optional ads SDKs). Foundation packages stayed at **2.0.4** after 2.0.4. The SDK package is **2.6.1**.

---

## Backlog (unscheduled)

These have no minor until the queued list above is empty or a maintainer inserts them into that list.

- Multi-device achievement merge (per-id, not full-blob last-write-wins)
- WriteLock-based Economy OCC helpers (still not idempotency)
- Stronger network status than `internetReachability`. Partial work shipped in 1.9.6+ (timeouts, soft circuit breaker, `GameServicesSync` in 1.9.7). A latency probe remains here. The offline-first façade is [2.10.0](#2.10.0), not this bullet.
- GDPR wipe completeness checklist automation

---

## Versioning cheat sheet

```
1.8.x        — hotfixes from bug-reports (closed: 1.8.7–1.8.11)
1.9.0        — prep minor after that hotfix stream (shipped)
1.9.x        — network / sync / IAP / auth patches (shipped)
1.10.0–1.10.4 — Economy Virtual Purchases (shipped; not Cloud Code)
1.11.0–1.11.2 — Economy Purchase Catalog (shipped; not entitlement verify)
1.12.0–1.12.5 — auth identities + IAP/ads patches (shipped; not Cloud Code)
2.0.0        — UPM monorepo (shipped)
2.0.4        — auth helpers + service-fault bridge (shipped)
2.1.x–2.6.0  — SDK patches and minors (shipped; foundation packages stayed 2.0.4)
2.6.1        — current SDK (optional LevelPlay / ATT)
2.7.0        — Cloud Code idempotent mutations ← next
2.8.0        — server-verified entitlements
2.9.0        — orphan cleanup / link merge helpers
2.10.0       — offline-first LocalReady
2.11.0       — unified single-flight surface
2.12.0       — headless reset / clear orchestrator
2.13.0       — headless conflict + account-link handoff
```
