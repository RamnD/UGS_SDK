# Security & correctness review

| | |
|---|---|
| **Found** | 2026-07-24 |
| **Affects** | 1.8.6 |
| **Status** | in-progress — Stages 1–3 → **1.8.7–1.8.9**; remaining M/L open |
| **Severity** | Critical + High batch (see table below) |
| **Branch** | main |
| **Scope** | 77 `.cs` files, ~4700 lines UGS; Auth, IAP, Economy, CloudSave, Items, Analytics, Ads, RemoteConfig, Leaderboard, Achievements |

Hotfixes for items below ship as **patch** releases (`1.8.7+`). Product epics (Cloud Code, server entitlements, …) live in [../ROADMAP.md](../ROADMAP.md).

### Hotfix tracking (1.8.x)

| ID | Title | Target patch | Status |
|----|-------|--------------|--------|
| C1 | Achievements wipe on failed load | 1.8.7 | **fixed-in 1.8.7** |
| C2 | Economy double flush / no single-flight | 1.8.8 | **fixed-in 1.8.8** |
| C3 | Auth DeleteAccount on AlreadyLinked | 1.8.7 | **fixed-in 1.8.7** (empty→Delete, else SignOut) |
| H1 | No client idempotency on balance ops | 1.8.8 (in_flight states); full idempotency → [ROADMAP 1.9.0](../ROADMAP.md) | partial — server dedupe still 1.9.0 |
| H2 | Entitlements from PendingOrders | 1.8.9 | **fixed-in 1.8.9** |
| H3 | Items cancel-after-grant refund exploit | 1.8.7 | **fixed-in 1.8.7** |
| H4 | Offline consumable grant lost on refresh | 1.8.9 | **fixed-in 1.8.9** |
| H5 | CloudSave ±1s version match | 1.8.9 | **fixed-in 1.8.9** |
| H6 | Ads overlapping shows lose callbacks | 1.8.7 | **fixed-in 1.8.7** |
| H7 | Ads close-before-reward fails grant | 1.8.7 | **fixed-in 1.8.7** |
| H8 | Enqueue during flush dropped | 1.8.8 | **fixed-in 1.8.8** |
| M4 | Error classification by substring | 1.8.8 | **fixed-in 1.8.8** |
| M8 | Item/consumable prefs not namespaced | 1.8.9 | **fixed-in 1.8.9** |
| M* / L* | Medium / Low batch | 1.8.x or later patches | open |

---

## Резюме

Секретов/токенов в репозитории и логах нет, `.gitignore` корректен для Unity-пакета.

Главная системная проблема: **несколько подсистем при сбое загрузки/сети затирают серверные данные игрока, а критичные для экономики решения принимаются по подстроке в тексте ошибки (нет типизированной классификации и ключей идемпотентности).**

Находки, помеченные *(проверено по коду)*, перепроверены вручную по исходникам, остальные — по результатам направленного ревью с указанием строк.

| Severity | Кол-во |
|----------|--------|
| 🔴 Critical | 3 |
| 🟠 High | 8 |
| 🟡 Medium | 11 |
| 🟢 Low | 10 |

---

## 🔴 CRITICAL

### C1. Achievements — сбой загрузки затирает все ачивки в облаке
**Файл:** `Runtime/UGS/Achievements/UGSAchievementService.cs:174-177` *(проверено по коду)*

`_isLoaded = true` ставится **до** `await LoadAllAsync()` (строка 188). Если загрузка упала (таймаут/5xx) или warmup вызван fire-and-forget без await, следующий `SetProgressAsync` считает состояние загруженным, пишет одну ачивку в пустой `_states`, и `FlushAsync` перезаписывает в Cloud Save **весь прогресс единственной записью**.

**Сценарий:** warmup один раз упал → вызывающий залогировал/проглотил исключение → позже `SetProgressAsync("first_win", 1, 1)` пишет одну ачивку в пустой `_states` → `FlushAsync` делает `SaveAsync({ [CloudSaveKey] = json })` только с этой ачивкой → все ранее заработанные ачивки в облаке стёрты.

**Фикс:** ставить `_isLoaded = true` только после успешной загрузки (внутри `try`, после заполнения `_states`), в `catch` — сбрасывать в `false` перед rethrow. Конкурентные загрузки защитить общим in-flight `Task`, а не bool-флагом.

---

### C2. Economy — двойное начисление на сервере при повторном/конкурентном flush
**Файлы:** `Runtime/UGS/Economy/UGSEconomyService.cs:44` + `Runtime/UGS/Economy/PendingTransactionQueue.cs:86-145`

`RefreshBalancesAsync` не имеет reentrancy-guard и вызывается минимум из двух мест: из игры (периодический/ручной sync) и внутренне после каждого IAP-redeem (`UGSRealMoneyPurchaseService.cs:313`). Два пересекающихся вызова на main-thread чередуются на точках `await`. `FlushAsync` начинается с `Load()`, читая дельты с диска, и только после цикла вызывает `PersistRemaining`. Оба конкурентных flush читают одну и ту же `+N`, оба шлют `IncrementBalanceAsync(GOLD, N)`. Сервер без клиентского idempotency-key применяет **оба → +2N**.

**Сценарий:** у игрока в очереди начисленная оффлайн валюта, он инициирует покупку (redeem→refresh), одновременно срабатывает периодический refresh → дельта применяется на сервере дважды. Игрок получает валюту, которую не зарабатывал/не оплачивал.

**Фикс:** single-flight (`SemaphoreSlim`/флаг) вокруг `RefreshBalancesAsync` + клиентский idempotency-key на каждую очередную транзакцию, чтобы серверные инкременты дедуплицировались.

---

### C3. Auth — попытка «связать аккаунт» безвозвратно удаляет текущий
**Файл:** `Runtime/UGS/Auth/UGSAuthService.cs:247-274` (особенно 260) *(проверено по коду)*

При `AccountAlreadyLinked` метод `SignIntoExistingAfterAlreadyLinkedAsync` **безусловно** вызывает `DeleteAccountAsync()` на текущем игроке. Docstring предполагает «свежий анонимус-заглушку», но это нигде не проверяется.

**Сценарий:** игрок неделями играл анонимно, накопив прогресс в Cloud Save / Economy, затем нажал «Войти через Google». Этот Google-идентификатор уже привязан к старому UGS-игроку (прошлое устройство/переустановка). SDK ловит `AccountAlreadyLinked` и вызывает `DeleteAccountAsync()` на **текущем** аккаунте → серверный аккаунт и Cloud Save удаляются навсегда → игрок вошёл в старый аккаунт, недавний прогресс потерян безвозвратно от действия, которое он считал «привязкой».

**Фикс:** на recovery-пути не удалять — использовать `SignOut(clearCredentials: true)`, чтобы анонимный аккаунт остался; либо сначала проверять, что текущий игрок действительно пустой/свежий; либо вернуть решение о конфликте в UI (`AccountLinkResult`), чтобы игра предупредила игрока до деструктивного действия.

---

## 🟠 HIGH

### H1. Economy — нет idempotency-key: таймаут-после-применения даёт двойное начисление/списание
**Файлы:** `Runtime/UGS/Economy/UGSEconomyService.cs:98-115, 150-173`; `PendingTransactionQueue.cs:107-113`

Если сервер применил инкремент, но ответ потерян (классифицируется как «recoverable»), код перевыкладывает `+amount` в очередь; следующий flush применяет его снова. Add → игрок получает 2×. Spend/`TryApplyLocalSpend` → списание дважды. `IncrementBalanceAsync`/`DecrementBalanceAsync` не несут клиентский transaction-id, поэтому ретраи не at-most-once.

**Фикс:** передавать стабильный idempotency/transaction-id на каждую логическую операцию.

---

### H2. IAP — энтайтлменты выдаются по неоплаченным pending-заказам
**Файл:** `Runtime/UGS/IAP/UGSRealMoneyPurchaseService.cs:361-363, 368`

Восстановление энтайтлментов идёт по `orders.PendingOrders` (отложенный/неподтверждённый платёж — Google «pending transactions», медленная карта, family approval). Гранта по pending-заказу означает выдачу товара до — и, возможно, без — оплаты. Если отложенный платёж провалится/отменится, энтайтлмент уже сохранён.

**Фикс:** восстанавливать энтайтлменты только из `ConfirmedOrders`.

---

### H3. Items — отмена покупки после серверной выдачи → возврат денег + товар остаётся (эксплойт)
**Файл:** `Runtime/UGS/Items/UGSItemService.cs:110-134` *(проверено по коду)*

`ThrowIfCancellationRequested()` на строке 114 стоит **после** успешного `AddInventoryItemAsync` (строка 113). Брошенный `OperationCanceledException` ловится широким `catch (Exception e)` (в отличие от consumable-сервиса, здесь нет фильтра OCE), и код рефандит валюту, хотя товар уже выдан сервером → игрок оставляет товар и получает деньги обратно.

Дополнительно:
- Рефанд `AddCurrencyAsync` использует тот же (уже отменённый) токен → откат может немедленно прерваться → валюта молча теряется (лог «incomplete», строка 129).
- `_ownedItems.Add(id)` не выполнился → `IsOwned(id)` = false → повтор снова запускает spend + grant → двойное списание/двойная выдача.

**Фикс:** не вызывать `ThrowIfCancellationRequested()` после закоммиченной серверной мутации; добавить отдельный `catch (OperationCanceledException)`, который rethrow'ит без рефанда, если грант уже прошёл; для компенсирующего рефанда использовать `CancellationToken.None`; проверять серверное владение перед рефандом (идемпотентность).

---

### H4. Items — оффлайн-грант расходников теряется при следующем sync
**Файл:** `Runtime/UGS/Items/UGSConsumableItemService.cs:152-161, 189`

Оффлайн `TryGrantAsync` пишет грант в кэш + PlayerPrefs и возвращает `true`, но **pending-очереди для реконсиляции с сервером нет**. Следующий онлайн `RefreshAsync` вызывает `RebuildFromBalances`, который делает `_quantities.Clear()` и перезаписывает чисто из серверных балансов + `SaveToPrefs()` → оффлайн-грант стирается из памяти и с диска навсегда.

**Сценарий:** игрок получил награду (Shield +5) оффлайн → UI показывает +5 → реконнект → `RefreshAsync` → сервер не знал про +5 → кэш перестроен по серверу → +5 исчезли.

**Фикс:** либо требовать сеть для грантов (как для consume), либо добавить durable pending-очередь (как описано в доке `IInventoryService`) и в `RebuildFromBalances` переприменять неслитые дельты вместо слепого затирания.

---

### H5. CloudSave — нечёткое сравнение таймстампов (±1с) как проверка версии
**Файл:** `Runtime/UGS/CloudSave/UGSCloudSaveService.cs:232-237`

`TimestampsMatch` считает равными любые два таймстампа в пределах 1 секунды, и этот же нечёткий чек управляет и dirty-проверкой, и optimistic-concurrency.

- **Проявление A (правка не выгружена):** `UploadLocalAsync` ставит `BaseTimestamp = LocalTimestamp = now`. Если игра вызовет `Set()` снова в пределах 1с, новый `LocalTimestamp` в толерансе от `BaseTimestamp` → `IsDirty == false` → `PushToCloudAsync` выходит на строке 155. Правка не отправлена и уязвима к затиранию последующим `ApplyCloud`.
- **Проявление B (кросс-клиентский clobber):** в `PushToCloudAsync` (строка 165) `TimestampsMatch(cloudTs, BaseTimestamp)` истинно → клиент A перезаписывает свежую запись клиента B без конфликта (silent last-write-wins).

**Фикс:** использовать точное равенство для проверки версии/родителя (сравнивать строку `__ts` или монотонный счётчик версии). Толеранс — только для косметического отображения, никогда для dirty/concurrency.

---

### H6. LevelPlay — перекрывающиеся запросы теряют колбэки
**Файл:** `Runtime/UGS/Ads/LevelPlay/LevelPlayAdsManager.cs:110-144, 117-124`

Отслеживается только один набор колбэков. Если `ShowRewardedAd(A, ...)` вызван пока A грузится, а затем `ShowRewardedAd(B, ...)`, поля `_pendingSuccess/_pendingFailed/_activeRewardedUnitId` перезаписываются на B. Когда A догрузится, `OnRewardedLoaded(A)` видит `A != _activeRewardedUnitId (B)` и не показывает/не фейлит A → вызывающий A не получает **ни `onSuccess`, ни `onFailed`** → вечное зависание. То же с единственными слотами `_deferredRewardedShow`/`_deferredInterstitialShow`.

**Фикс:** отклонять/фейлить новый запрос пока активен предыдущий (сразу вызвать `onFailed`) либо ставить в очередь; не затирать pending-колбэки молча.

---

### H7. LevelPlay — награда теряется, если `OnAdClosed` пришёл раньше `OnAdRewarded`
**Файл:** `Runtime/UGS/Ads/LevelPlay/LevelPlayAdsManager.cs:267-285`

Дизайн предполагает, что `OnAdRewarded` всегда до `OnAdClosed`. На адаптерах медиации, доставляющих close до reward (документировано как adapter-dependent в ironSource/LevelPlay), `OnRewardedClosed` срабатывает первым: вызывает `onFailed` и сбрасывает `_activeRewardedUnitId = null`. Затем `OnRewardEarned` видит `adUnitId != null` и делает `return` → игрок досмотрел рекламу, но получил `onFailed` без награды.

**Фикс:** флаг `bool _rewardEarned`, выставляемый в `OnRewardEarned`; в `OnRewardedClosed` вызывать `onFailed` только если награда не заработана, и выдавать награду при её чуть более позднем приходе.

---

### H8. Economy — enqueue во время flush молча теряется
**Файлы:** `Runtime/UGS/Economy/PendingTransactionQueue.cs:118/127/142/159` (`PersistRemaining`) vs `Enqueue:37-79`

`FlushAsync` держит in-memory снапшот очереди (строка 88). Конкурентный `Enqueue` (из recoverable-сбоя Add/Spend во время онлайн-flush) делает свой `Load()`→modify→`Persist()` на диск. Затем flush вызывает `PersistRemaining(queue, processed)` и пишет **устаревший** снапшот минус обработанные → новая дельта затирается. Игрок теряет валюту, заработанную во время flush.

**Фикс:** перечитывать и мержить перед записью хвоста, либо сериализовать доступ enqueue/flush.

---

## 🟡 MEDIUM

### M1. Энтайтлменты клиент-авторитетны, серверно не верифицируются
**Файлы:** `Runtime/Core/IAP/CloudSaveEntitlementStore.cs`, `UGSRealMoneyPurchaseService.cs:248`
Энтайтлменты — обычный набор строк через `ICloudSaveService` (клиент-записываемый), читается `HasEntitlement` без проверки чека/сервера. Для продуктов с `RedeemWithEconomy = false` серверной валидации нет вообще → правкой Cloud Save игрок выдаёт себе `vip`/`season_pass` бесплатно. **Фикс:** валидировать против серверных чеков/Economy, локальный набор — только кэш.

### M2. `SignInAsync` инициализирует UnityServices без environment
**Файл:** `UGSAuthService.cs:129-133`
`await UnityServices.InitializeAsync()` без `InitializationOptions`/environment. Если вызвать напрямую (не через `UGSServicesBuilder.BuildAsync`, где резолвится environment), staging-билд молча аутентифицируется в **prod**-окружении. **Фикс:** централизовать инициализацию (resolve + set environment) в общем хелпере для обоих путей.

### M3. `ToMinorUnits` жёстко умножает на 100
**Файл:** `UGSRealMoneyPurchaseService.cs:406-410`
Хардкод 2 знаков: JPY/KRW (0 знаков) раздуваются ×100, 3-знаковые (BHD) неверны, большие цены переполняют `int`. Значение идёт в `RedeemGooglePlayStorePurchaseArgs`/`RedeemAppleAppStorePurchaseArgs` → искажённая аналитика real-money spend (не источник гранта — чек серверно валидируется). **Фикс:** брать экспоненту minor-unit из ISO-валюты, использовать `long`.

### M4. Классификация ошибок по подстроке текста
**Файл:** `Runtime/UGS/Economy/EconomyErrorClassifier.cs:36-40, 62`
Для прочих reason и обёрнутых исключений recoverability решается сканом текста на `"network"`, `"unavailable"`, `"http 5"`, `"503"`. `"http 5"` матчит и перманентную `500` → бесконечный requeue/оптимистичное применение и расхождение кэша с сервером. **Фикс:** классифицировать по типизированным reason/статус-кодам.

### M5. Analytics: числа сериализуются/парсятся в текущей культуре
**Файл:** `Runtime/UGS/Analytics/AnalyticsEventSerializer.cs:83, 103-110`
`value.ToString()` и `float/double/int.TryParse` без `CultureInfo.InvariantCulture`. `1.5` в locale с запятой → `"1,5"`, после смены локали `float.TryParse("1,5")` → `15`. `UGSRemoteConfigService` уже использует `InvariantCulture` — привести к тому же.

### M6. GPGS: TCS без cancellation-регистрации и без `RunContinuationsAsynchronously`
**Файл:** `UGSAuthService.cs:393-451`
Токен только опрашивается внутри колбэков GPGS, не зарегистрирован на TCS → отмена при открытом sign-in листе не разблокирует await (вечный hang). Континуация выполняется inline на потоке колбэка GPGS (реентрантность, off-main-thread вызовы UGS). **Фикс:** `cancellationToken.Register(() => tcs.TrySetCanceled(...))` + `TaskCreationOptions.RunContinuationsAsynchronously`.

### M7. `IGameServices.IsAuthenticated` — застывший снимок
**Файл:** `Runtime/UGS/Bootstrap/UGSGameServices.cs:44`
`IsAuthenticated = auth.IsSignedIn` фиксируется в конструкторе. После `DeleteAccountAsync`/`Reset`/logout/истечения токена live-значение false, а свойство возвращает true. Ранний `GameServicesLocator.Set` (builder:166) создаёт инстанс до sign-in → он навсегда `IsAuthenticated=false`. **Фикс:** live-свойство `=> Auth?.IsSignedIn ?? false`.

### M8. Item/Consumable кэши не разделены по generic-типу
**Файлы:** `UGSConsumableItemService.cs:17`, `UGSItemService.cs:15`
Константный PlayerPrefs-ключ (`consumables_currency_cache` / `items_owned_cache`) общий для всех инстанциаций `TItem`. Два enum'а пишут один blob → взаимное затирание кэша (`LoadFromPrefs` молча отбрасывает записи, не парсящиеся в `TItem`, скрывая порчу). CloudSave делает namespace по `typeof(TKey).Name` — сделать так же (`typeof(TItem).Name`).

### M9. Analytics pending-queue: без блокировок + O(n²) слив + дроп старейших
**Файл:** `Runtime/UGS/Analytics/PendingAnalyticsQueue.cs:14-61, 25-31`
`Enqueue`/`TryDequeue` делают read-modify-write всего массива в PlayerPrefs без локов. `DrainQueue` вызывает `TryDequeue` на каждое событие, каждый пересериализует весь массив + `PlayerPrefs.Save()` → слив 500 событий = 500 сериализаций + flush'ей. При переполнении молча дропаются **старейшие** события (обычно вход воронки/`session_start`). **Фикс:** load один раз, слив в памяти, persist один раз; лог при тримминге.

### M10. Профанити-фильтр обходится Unicode-гомоглифами
**Файл:** `UGSAuthService.cs:108-119`
`char.IsLetterOrDigit` пропускает буквы любого скрипта, а бан-лист сравнивается ordinal-ASCII-подстрокой → `nаzi` с кириллической `а` (U+0430) проходит. **Фикс:** NFKC-нормализация (и/или confusable-маппинг) перед сравнением, либо ограничить allow-list конкретным скриптом.

### M11. Резервный ключ `__ts` без валидации маппера
**Файл:** `UGSCloudSaveService.cs:283-286`
`UploadLocalAsync` безусловно перезаписывает `cloudData["__ts"]`. Если `ISaveKeyMapper.ToCloudKey` вернёт `"__ts"`, пользовательское значение молча заменяется таймстампом, а на загрузке уходит в `_cloudSnapshotTimestamp` → ключ исчезает из данных. **Фикс:** валидировать/отклонять ключи, равные `TimestampCloudKey`.

---

## 🟢 LOW

- **`Get<TValue>` глотает ошибки десериализации** (`UGSCloudSaveService.cs:56-63`): возврат `defaultValue` неотличим от «ключ отсутствует»; read-modify-write превращает временную порчу в перманентную потерю. Различать missing/unparseable.
- **Полный payload Cloud Save в `Debug.Log`** (`UGSCloudSaveService.cs:93-95, 288-290`): ключи+значения в открытом виде в логах устройства. Гейтить под debug-флаг, логировать только имена/количество.
- **Имя игрока в логах** (`UGSAuthService.cs:77, 82`): PII. Токены/credentials — не логируются (проверено).
- **`BannedPattern.IsMatch` без `RegexMatchTimeout`** (`UGSAuthService.cs:117-119`): ReDoS/фриз main-thread при вызове на каждый ввод.
- **`UpdateFromServer` обнуляет валюты, отсутствующие в ответе** (`BalanceCache.cs:69`): `_data[type] = item?.Balance ?? 0L`.
- **redeem-успешен-но-confirm-упал → возвращает false при уже выданной валюте** (`UGSRealMoneyPurchaseService.cs:249-256`): заказ остаётся pending и re-redeem'ится на следующем запуске.
- **Leaderboard/Achievements 404 по подстроке `"404"`** (`UGSLeaderboardService.cs:84-90`): неродственная ошибка с `404` в тексте → неверная классификация «нет записи». Проверять типизированный статус.
- **`JsonUtility.FromJson("{}")` может дать null-список** (`UGSConsumableItemService.cs:241-243`, `UGSItemService.cs:147-150`): `?? new()` покрывает только null-корень → NRE при `foreach`. Гвардить список (`entries ??= new()`).
- **`TestAdsManager` через `async void` безусловно выдаёт награду** (`TestAdsManager.cs:18-24`): `onFailed` мёртв, исключения ненаблюдаемы. Гейтить под `#if UNITY_EDITOR`/`DEVELOPMENT_BUILD`.
- **Корректный `AchievementStateCollection.items = _states` по ссылке** — конкурентная мутация во время сериализации flush возможна (следствие C1/H-race).

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

**1.8.x hotfix stream** (this report):

1. ~~**C1, C3, H3, H6/H7**~~ → **1.8.7**
2. ~~**C2 + H8 + M4**~~ → **1.8.8** (queue in_flight; full server idempotency → 1.9.0)
3. ~~**H4, H5, H2**~~ → **1.8.9** (+ M8 prefs namespace)
4. **M2 и прочий M/L** — точечные патчи по мере.

**Не в 1.8.x** (см. [ROADMAP](../ROADMAP.md)):

- Полная серверная идемпотентность Economy → **1.9.0** Cloud Code  
- Серверная верификация энтайтлментов (M1) → **1.10.0**  
- Orphan cleanup после link → **1.11.0**
