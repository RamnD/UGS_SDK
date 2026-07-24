# Roadmap — RamnD Game Services SDK

Versioned **product epics**. Each epic ships as a **minor** release: `1.N.0`.

| Stream | Where | Versioning |
|--------|--------|------------|
| Hotfixes / correctness bugs | [bug-reports/](./bug-reports/README.md) | Patch `1.8.x` (current), then patches on whatever minor is current |
| Planned features / architecture | **this file** | Minor `1.N.0` |

Current package: **1.8.6**. Urgent security/correctness work from 2026-07-24 stays on the **1.8.x hotfix stream** — it is **not** listed here as epics.

---

## [1.9.0] — Cloud Code: server-authoritative mutations

**Why:** Client Economy has no idempotency keys; true at-most-once grants and safe account merge need a server.

### Scope
- Cloud Code modules for: currency grant/spend with **idempotency key**, IAP post-redeem hooks, achievement merge if needed
- Client SDK: call Cloud Code instead of raw `IncrementBalanceAsync` for sensitive ops
- Durable server-side dedupe store (transaction id → applied)
- Docs: trust boundary (what stays client-optimistic vs must be server)

### Out of scope for 1.9.0
- Full anti-cheat / replay protection beyond idempotent writes
- Admin orphan-cleanup UI (see 1.11.0)

### Depends on
- Unity Cloud Code project wiring in consuming games (Maze)
- Stable client queue `pending → in_flight → done` from 1.8.x hotfixes

---

## [1.10.0] — Server-verified entitlements

**Why:** Cloud Save entitlements are client-writable (`RedeemWithEconomy = false` / VIP-style flags).

### Scope
- Validate paid entitlements against store receipts / Economy redeem / Cloud Code
- Local entitlement set = cache only
- Maze: no-ads and similar stay restore-from-store + server confirm

### Depends on
- 1.9.0 Cloud Code (or Economy redeem-only policy enforced in docs + game)

---

## [1.11.0] — Account link: orphan cleanup & merge helpers

**Why:** Client cannot `DeleteAccount` on the previous anonymous player after `SignIntoExisting`. Empty-check + SignOut leaves orphans.

### Scope
- Cloud Code / Admin API: delete orphan `playerId` list reported by clients
- Optional merge helpers: push snapshot metadata, economy reconcile rules
- Auth SDK: report `PreviousPlayerId` on recover for cleanup queue
- Docs: empty → Delete; non-empty → SignOut + conflict (client policy remains)

### Depends on
- 1.9.0
- Maze SaveConflict + post-link IAP restore (game-side, can land earlier)

---

## [1.12.0] — Offline-first façade (LocalReady)

**Why:** Games like Maze should play without blocking forever on UGS bootstrap; ads/IAP hidden offline, pools flush on reconnect.

### Scope
- SDK: clearer `LocalReady` / sync signals (or documented game pattern + optional helpers)
- Single reconnect flush entrypoint (economy pending, cloud push, analytics drain)
- `playerId` guard on durable queues when session changes
- Sample / docs for degraded mode

### Note
Maze may ship UX (preloader timeout, hide IAP offline) on game side before this minor; this epic standardizes it in the package.

---

## [1.13.0] — Mutation single-flight API surface

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
1.8.x  — hotfixes from bug-reports (current urgency)
1.9.0  — Cloud Code idempotent mutations
1.10.0 — server-verified entitlements
1.11.0 — orphan cleanup / link merge helpers
1.12.0 — offline-first LocalReady in SDK
1.13.0 — unified single-flight surface
```

When starting an epic, bump `package.json` to `1.N.0`, add CHANGELOG section, and link the epic heading here as **shipped**.
