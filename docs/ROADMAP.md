# Roadmap — RamnD Game Services SDK

Versioned **product epics**. Each epic ships as a **minor** release: `1.N.0`.

| Stream | Where | Versioning |
|--------|--------|------------|
| Hotfixes / correctness bugs | [bug-reports/](./bug-reports/README.md) | Patch on current minor (was `1.8.x`; next patches on `1.9.x`) |
| Planned features / architecture | **this file** | Minor `1.N.0` |

Current package: **1.9.2**. The 2026-07-24 security/correctness review is **closed** on **1.8.7–1.8.11**. Prep **1.9.0**; IAP harden **1.9.1**; post-audit leftovers **1.9.2**. **Server / Cloud Code** starts at **1.10.0**.

---

## [1.9.0] — Prep minor (post–1.8.x) — **shipped**

**Why:** Open the next minor cleanly after the large 1.8.x correctness stream, without bundling Cloud Code yet.

### Scope (this release)
- Version bump `1.8.11` → `1.9.0`
- ROADMAP realign: server-authoritative mutations deferred to **1.10.0**
- Docs cross-links updated (bug-report / economy → new epic versions)
- Baseline for `1.9.x` patches while Cloud Code is designed/wired in games

### Out of scope for 1.9.0
- Cloud Code modules and server dedupe store → **1.10.0**
- Server-verified entitlements → **1.11.0**

### Depends on
- Closed 1.8.x hotfix stream (client queue `pending → in_flight → done`, ads/IAP/CloudSave hardening)

---

## [1.10.0] — Cloud Code: server-authoritative mutations

**Why:** Client Economy has no server idempotency; true at-most-once grants and safe account merge need a server.

### Scope
- Cloud Code modules for: currency grant/spend with **idempotency key**, IAP post-redeem hooks, achievement merge if needed
- Client SDK: call Cloud Code instead of raw `IncrementBalanceAsync` for sensitive ops (reuse durable row `id` from 1.8.x queue)
- Durable server-side dedupe store (transaction id → applied)
- Docs: trust boundary (what stays client-optimistic vs must be server)

### Out of scope for 1.10.0
- Full anti-cheat / replay protection beyond idempotent writes
- Admin orphan-cleanup UI (see 1.12.0)

### Depends on
- Unity Cloud Code project wiring in consuming games (Maze)
- Stable client queue from 1.8.x / prep 1.9.0

---

## [1.11.0] — Server-verified entitlements

**Why:** Cloud Save entitlements are client-writable (`RedeemWithEconomy = false` / VIP-style flags).

### Scope
- Validate paid entitlements against store receipts / Economy redeem / Cloud Code
- Local entitlement set = cache only
- Maze: no-ads and similar stay restore-from-store + server confirm

### Depends on
- 1.10.0 Cloud Code (or Economy redeem-only policy enforced in docs + game)

---

## [1.12.0] — Account link: orphan cleanup & merge helpers

**Why:** Client cannot `DeleteAccount` on the previous anonymous player after `SignIntoExisting`. Empty-check + SignOut leaves orphans.

### Scope
- Cloud Code / Admin API: delete orphan `playerId` list reported by clients
- Optional merge helpers: push snapshot metadata, economy reconcile rules
- Auth SDK: report `PreviousPlayerId` on recover for cleanup queue
- Docs: empty → Delete; non-empty → SignOut + conflict (client policy remains)

### Depends on
- 1.10.0
- Maze SaveConflict + post-link IAP restore (game-side, can land earlier)

---

## [1.13.0] — Offline-first façade (LocalReady)

**Why:** Games like Maze should play without blocking forever on UGS bootstrap; ads/IAP hidden offline, pools flush on reconnect.

### Scope
- SDK: clearer `LocalReady` / sync signals (or documented game pattern + optional helpers)
- Single reconnect flush entrypoint (economy pending, cloud push, analytics drain)
- `playerId` guard on durable queues when session changes
- Sample / docs for degraded mode

### Note
Maze may ship UX (preloader timeout, hide IAP offline) on game side before this minor; this epic standardizes it in the package.

---

## [1.14.0] — Mutation single-flight API surface

**Why:** Ads/IAP/CloudSave/Auth need one consistent “busy → reject” contract beyond ad-hoc fixes.

### Scope
- Documented single-flight guarantees per service
- Ads: busy reject + reward/close FSM (if any gaps remain after 1.8.x)
- Optional shared `InFlightGate` helper in Core
- Analytics drain locking + optional client event-id dedupe hooks

### Note
Critical ads/economy races are fixed in **1.8.x** hotfixes; this epic is API polish and consistency.

---

## Backlog (unscheduled)

- Multi-device achievement merge (per-id, not full-blob LWW)
- WriteLock-based Economy OCC helpers (still not idempotency)
- Stronger NetworkStatus than `internetReachability`
- GDPR wipe completeness checklist automation

---

## Versioning cheat sheet

```
1.8.x  — hotfixes from bug-reports (closed: 1.8.7–1.8.11)
1.9.0  — prep minor after hotfix stream (this release)
1.10.0 — Cloud Code idempotent mutations (server)
1.11.0 — server-verified entitlements
1.12.0 — orphan cleanup / link merge helpers
1.13.0 — offline-first LocalReady in SDK
1.14.0 — unified single-flight surface
```

When starting an epic, bump `package.json` to `1.N.0`, add CHANGELOG section, and link the epic heading here as **shipped**.
