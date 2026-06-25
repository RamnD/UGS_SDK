# Инициализация и Bootstrap

← [README](../../README.md) | 🇬🇧 [English version](../bootstrap.md)

---

## Обзор

Все сервисы создаются один раз при старте через `UGSServicesBuilder` (продакшн) или `MockGameServices.CreateDefault()` (редактор / тесты). Оба пути регистрируют результат в `GameServicesLocator`.

Обобщённые сервисы (`IInventoryService<T>`, `IItemService<T>`, `ICloudSaveService<TKey>`) находятся **вне фасада** — создавайте их в колбэке `OnAuthenticated` и храните в своём игровом бутстрапе.

---

## UGSServicesBuilder — полный пример

```csharp
// ServicesBootstrap.cs (MonoBehaviour)
[SerializeField] private ProfanityConfig _profanityConfig;
[SerializeField] private bool _forceAnonymous = true;

private IInventoryService<CurrencyType> _economy;
private ICloudSaveService<SaveKey>     _cloudSave;

private async void Start()
{
    var services = await new UGSServicesBuilder()
        // ── Auth-параметры ────────────────────────────────────────────
        .WithForceAnonymous(_forceAnonymous)            // true = всегда анонимно (для дева)
        .WithAuthProviderCredentials(new GameServicesAuthProviderConfig
        {
            GooglePlayGamesOAuthWebClientId = "OAUTH_CLIENT_ID",
            AppleServicesId = "com.yourcompany.yourgame",
        })
        // ── Фильтр имён ───────────────────────────────────────────────
        .WithNameValidator(_profanityConfig?.ToValidatorConfig())   // наивысший приоритет
        // .WithProfanityFilter("мат1", "мат2")                     // альтернатива: список
        // .WithProfanityFilter(new Regex(@"шаблон"))               // альтернатива: regex
        // ── Реклама ───────────────────────────────────────────────────
        .WithAds(new LevelPlayAdsManager("APP_KEY"))
        // ── Хук после входа: создаём проектные сервисы ───────────────
        .OnAuthenticated(async auth =>
        {
            _economy   = new UGSEconomyService<CurrencyType>(new CurrencyMapper());
            _cloudSave = new UGSCloudSaveService<SaveKey>(new SaveKeyMapper());

            await _economy.RefreshBalancesAsync();
            var conflict = await _cloudSave.LoadAsync();
            if (conflict.HasValue)
                _cloudSave.ApplyCloud(); // или показать диалог конфликта
        })
        .BuildAsync(destroyCancellationToken);

    // GameServicesLocator устанавливается внутри BuildAsync
    IsReady = true;
}
```

### Справка по методам билдера

| Метод | Описание |
|-------|----------|
| `WithForceAnonymous(bool)` | Пропустить платформенный вход; всегда анонимно |
| `WithAuthProviderCredentials(cfg)` | OAuth-идентификаторы Google Play / Apple |
| `WithNameValidator(NameValidatorConfig)` | Полная конфигурация валидатора (слова + regex). Перекрывает `WithProfanityFilter`. |
| `WithProfanityFilter(string[])` | Только список запрещённых слов |
| `WithProfanityFilter(Regex)` | Только запрещённый паттерн |
| `WithProfanityFilter(ProfanityConfig)` | ScriptableObject (редактируется в Inspector) |
| `WithAds(IAdsManager)` | Менеджер рекламы (LevelPlay, UnityAds, TestAds…) |
| `OnAuthenticated(Func<IAuthService, Task>)` | Колбэк после успешного входа |
| `BuildAsync(CancellationToken)` | Инициализирует UGS, входит, запускает колбэк, устанавливает локатор |

---

## Mock (редактор / офлайн тесты)

```csharp
var services = MockGameServices.CreateDefault();
// Auth уже выполнен. Analytics, Ads, Leaderboards — no-op заглушки.

var economy   = new MockInventoryService<CurrencyType>();
var cloudSave = new MockCloudSaveService<SaveKey>();
```

Mock-сервисы реализуют те же интерфейсы — UI и игровая логика не меняются.

---

## Доступ к сервисам во время игры

```csharp
// Безопасный доступ с null-проверкой:
if (GameServicesLocator.TryGet(out var svc))
{
    svc.Analytics?.LogEvent(new LevelStartedEvent { Level = 3 });
    svc.Leaderboards?.SubmitScoreAsync("run_leaderboard", score);
}

// Прямой доступ (null до завершения BuildAsync):
GameServicesLocator.Services?.Auth.GetPlayerName();
```

---

## NetworkStatus (имитация офлайна)

```csharp
NetworkStatus.ForceOffline = true;   // имитировать отсутствие сети в редакторе
bool online = NetworkStatus.IsOnline; // true если есть сеть И !ForceOffline
```

---

## Правила потоков

`BuildAsync` использует `ConfigureAwait(false)`, поэтому его внутренние продолжения выполняются в **threadpool**. Колбэк `OnAuthenticated` вызывается именно оттуда.

**Что можно/нельзя в `OnAuthenticated`:**
- ✅ `await SomeUgsApiAsync()` — ок
- ✅ `new UGSCloudSaveService(mapper)` — ок (конструктор безопасен; PlayerPrefs загружаются лениво)
- ❌ `PlayerPrefs.GetString(...)` — крэш; только с main thread
- ❌ `GetComponent<T>()` / `Instantiate` / `FindObjectOfType` — крэш

Если нужно обратиться к Unity API внутри `OnAuthenticated`, верните управление на main thread:
```csharp
.OnAuthenticated(async auth =>
{
    await UniTask.SwitchToMainThread(); // если используется UniTask
    // или
    await Task.Yield();                 // возвращает управление на Unity SyncContext
    PlayerPrefs.GetString("key");       // теперь безопасно
})
```
