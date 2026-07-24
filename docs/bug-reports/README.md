# Bug reports

Dated findings and hotfix tracking for the SDK.

**Not** a product roadmap — see [../ROADMAP.md](../ROADMAP.md) for planned `1.N.0` epics (Cloud Code, etc.).

## Naming

```
YYYY-MM-DD-short-slug.md
```

`YYYY-MM-DD` = **when the issue was found / reported** (not when it shipped).

## Report header (required)

Every report starts with:

| Field | Meaning |
|-------|---------|
| **Found** | Discovery date |
| **Affects** | Package version(s) where the bug exists (e.g. `1.8.6`) |
| **Status** | `open` · `in-progress` · `fixed-in 1.8.x` · `wontfix` · `deferred-to 1.N.0` |
| **Severity** | Critical / High / Medium / Low |

Update **Status** when a fix lands in CHANGELOG; keep the original **Found** date.

## Index

| Found | Report | Affects | Status |
|-------|--------|---------|--------|
| 2026-07-24 | [Security & correctness review](./2026-07-24-security-correctness-review.md) | 1.8.6 | **closed** (→ **1.8.11**; epics in ROADMAP) |
