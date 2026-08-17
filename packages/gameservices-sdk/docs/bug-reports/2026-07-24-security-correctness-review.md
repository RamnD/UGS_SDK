# Security & correctness review

| | |
|---|---|
| **Found** | 2026-07-24 |
| **Affects** | 1.8.6 |
| **Status** | **closed** — hotfix stream **1.8.7–1.8.11**; product epics → [ROADMAP](../ROADMAP.md) |
| **Severity** | Critical + High batch (see table below) |
| **Branch** | main |
| **Scope** | 77 `.cs` files, ~4700 lines UGS; Auth, IAP, Economy, CloudSave, Items, Analytics, Ads, RemoteConfig, Leaderboard, Achievements |

Hotfixes for items below shipped as **patch** releases (`1.8.7`–`1.8.11`). Product epics (Cloud Code, server entitlements, …) live in [../ROADMAP.md](../ROADMAP.md).

### Hotfix tracking (1.8.x)

| ID | Title | Target patch | Status |
|----|-------|--------------|--------|
| C1 | Achievements wipe on failed load | 1.8.7 | **fixed-in 1.8.7** |
| C2 | Economy double flush / no single-flight | 1.8.8 | **fixed-in 1.8.8** |
| C3 | Auth DeleteAccount on AlreadyLinked | 1.8.7 | **fixed-in 1.8.7** (empty→Delete, else SignOut) |
| H1 | No client idempotency on balance ops | 1.8.8 (in_flight); full → [ROADMAP 1.10.0](../ROADMAP.md) | **partial** — client queue done; server dedupe 1.10.0 |
| H2 | Entitlements from PendingOrders | 1.8.9 | **fixed-in 1.8.9** |
| H3 | Items cancel-after-grant refund exploit | 1.8.7 | **fixed-in 1.8.7** |
| H4 | Offline consumable grant lost on refresh | 1.8.9 | **fixed-in 1.8.9** |
| H5 | CloudSave ±1s version match | 1.8.9 | **fixed-in 1.8.9** |
| H6 | Ads overlapping shows lose callbacks | 1.8.7 | **fixed-in 1.8.7** |
| H7 | Ads close-before-reward fails grant | 1.8.7 | **fixed-in 1.8.7** |
| H8 | Enqueue during flush dropped | 1.8.8 | **fixed-in 1.8.8** |
| M1 | Server entitlement verify | → [ROADMAP 1.11.0](../ROADMAP.md) | deferred |
| M2 | SignIn without environment | 1.8.10 | **fixed-in 1.8.10** |
| M3 | ToMinorUnits ×100 hardcode | 1.8.10 | **fixed-in 1.8.10** |
| M4 | Error classification by substring | 1.8.8 | **fixed-in 1.8.8** |
| M5 | Analytics culture-dependent numbers | 1.8.10 | **fixed-in 1.8.10** |
| M6 | GPGS TCS cancel / sync continuations | 1.8.10 | **fixed-in 1.8.10** |
| M7 | IsAuthenticated frozen snapshot | 1.8.10 | **fixed-in 1.8.10** |
| M8 | Item/consumable prefs not namespaced | 1.8.9 | **fixed-in 1.8.9** |
| M9 | Analytics queue lock / batch drain | 1.8.10 | **fixed-in 1.8.10** |
| M10 | Profanity Unicode homoglyphs | 1.8.11 | **fixed-in 1.8.11** |
| M11 | `__ts` mapper collision | 1.8.10 | **fixed-in 1.8.10** |
| L1 | CloudSave Get swallows corrupt JSON | 1.8.11 | **fixed-in 1.8.11** (`TryGet` + throw) |
| L2 | CloudSave full payload in logs | 1.8.11 | **fixed-in 1.8.11** |
| L3 | Player name in Auth logs (PII) | 1.8.11 | **fixed-in 1.8.11** |
| L4 | BannedPattern without MatchTimeout | 1.8.10 | **fixed-in 1.8.10** |
| L5 | BalanceCache zeros missing currencies | 1.8.11 | **fixed-in 1.8.11** |
| L6 | IAP confirm fail after redeem → false | 1.8.11 | **fixed-in 1.8.11** |
| L7 | Leaderboard 404 by substring | 1.8.10 | **fixed-in 1.8.10** |
| L8 | JsonUtility null list NRE | 1.8.9 / 1.8.11 | **fixed-in** (`??=` guards) |
| L9 | TestAdsManager always rewards | 1.8.10 | **fixed-in 1.8.10** |
| L10 | Achievements serialize by reference | 1.8.11 | **fixed-in 1.8.11** (deep snapshot) |

---

## Резюме

Секретов/токенов в репозитории и логах нет, `.gitignore` корректен для Unity-пакета.

Главная системная проблема на момент аудита: **несколько подсистем при сбое загрузки/сети затирали серверные данные игрока, а критичные для экономики решения принимались по подстроке в тексте ошибки.** Клиентский hotfix-stream **1.8.7–1.8.11** закрыл Critical/High/Medium/Low из этого отчёта; серверные эпики (идемпотентность Economy, entitlements) вынесены в ROADMAP.

Находки, помеченные *(проверено по коду)*, перепроверены вручную по исходникам, остальные — по результатам направленного ревью с указанием строк.

| Severity | Кол-во | Outcome |
|----------|--------|---------|
| 🔴 Critical | 3 | fixed in 1.8.7–1.8.8 |
| 🟠 High | 8 | fixed / H1 partial→1.10.0 |
| 🟡 Medium | 11 | fixed; M1 deferred→1.11.0 |
| 🟢 Low | 10 | fixed in 1.8.10–1.8.11 |

---

## 🔴 CRITICAL

### C1. Achievements: failed load → empty cache → Flush wipes Cloud Save *(проверено по коду)*
**Файл:** `Runtime/UGS/Achievements/UGSAchievementService.cs`  
**Fixed in 1.8.7** — `_isLoaded` only after successful path; flush requires cloud baseline.

### C2. Economy: concurrent Flush / no single-flight *(проверено по коду)*
**Файл:** `Runtime/UGS/Economy/PendingTransactionQueue.cs`  
**Fixed in 1.8.8** — single-flight + `pending → in_flight`. Full server idempotency → 1.10.0.

### C3. Auth link: DeleteAccount on AlreadyLinked *(проверено по коду)*
**Файл:** `Runtime/UGS/Auth/UGSAuthService.cs`  
**Fixed in 1.8.7** — empty Cloud Save → Delete; else SignOut before SignedIntoExisting.

---

## 🟠 HIGH

### H1. No client idempotency on balance ops
**Partial in 1.8.8** (durable in_flight). **Server dedupe → [ROADMAP 1.10.0](../ROADMAP.md).**

### H2–H8
All **fixed** in 1.8.7–1.8.9 (see tracking table). Details retained in git history of this file / CHANGELOG.

---

## 🟡 MEDIUM

### M1. Энтайтлменты клиент-авторитетны
**Deferred** → [ROADMAP 1.11.0](../ROADMAP.md).

### M2–M11
All **fixed** in 1.8.8–1.8.11 except M1 (deferred). M10 (homoglyphs) → **1.8.11**.

---

## 🟢 LOW

All ten Low items **fixed** in 1.8.10–1.8.11 (see tracking table L1–L10).

---

## ✅ Что сделано хорошо

- Секретов/токенов в коде и логах нет; `serverAuthCode`, Apple `identityToken`, Game Center signature/salt не логируются.
- `OperationCanceledException` в большинстве мест корректно ре-throw'ится, не проглатывается.
- Онлайн spend/consume server-authoritative (`DecrementBalanceAsync` возвращает новый баланс; 422 → refresh) — нет клиентского double-spend против сервера.
- RemoteConfig парсит числа через `NumberStyles.Float, InvariantCulture` с fallback на дефолты.
- `ToIntQuantity` клампит отрицательные к 0 и overflow к `int.MaxValue`.
- CloudSave namespace'ит ключи по `typeof(TKey).Name`.
- Analytics: обработка enum согласована между live- и queued-путём; маппинг `valueType`/`nameof` совпадает с `GetType().Name`.
- `AnalyticsCustomEventEnricher` гвардит null/empty/"unknown" player id перед добавлением `ugs_player_id`.
- `.gitignore` корректен; собранных бинарников/секретов в репозитории нет.

---

## Приоритет исправлений

**1.8.x hotfix stream** — **complete** (`1.8.7`–`1.8.11`):

1. ~~**C1, C3, H3, H6/H7**~~ → **1.8.7**
2. ~~**C2 + H8 + M4**~~ → **1.8.8**
3. ~~**H4, H5, H2**~~ → **1.8.9**
4. ~~**M2/M3/M5/M6/M7/M9/M11 + L4/L7/L9**~~ → **1.8.10**
5. ~~**M10 + remaining L**~~ → **1.8.11**

**Не в 1.8.x** (см. [ROADMAP](../ROADMAP.md)):

- Полная серверная идемпотентность Economy → **1.10.0** Cloud Code  
- Серверная верификация энтайтлментов (M1) → **1.11.0**  
- Orphan cleanup после link → **1.12.0**
- Prep minor after 1.8.x hotfixes → **1.9.0** (shipped; no server modules)
