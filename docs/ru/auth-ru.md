# Аутентификация

← [README](../../README.md) | 🇬🇧 [English version](../auth.md)

---

## Необходимые платформенные плагины

UGS-слой аутентификации использует платформенные SDK, которых **нет в реестре UPM** — их нужно импортировать вручную.

### Android — Google Play Games Plugin v2.1.0

| | |
|---|---|
| **Страница релиза** | https://github.com/playgameservices/play-games-plugin-for-unity/releases/tag/v2.1.0 |
| **Прямая ссылка** | [`GooglePlayGamesPlugin-2.1.0.unitypackage`](https://github.com/playgameservices/play-games-plugin-for-unity/releases/download/v2.1.0/GooglePlayGamesPlugin-2.1.0.unitypackage) |

1. Импортировать через **Assets → Import Package → Custom Package**.
2. **Window → Google Play Games → Setup → Android Setup** — вставить OAuth Web Client ID (из Google Cloud Console, тип **Web application**).
3. Передать тот же ID в билдер:
```csharp
.WithAuthProviderCredentials(new GameServicesAuthProviderConfig
{
    GooglePlayGamesOAuthWebClientId = "YOUR_WEB_CLIENT_ID.apps.googleusercontent.com",
})
```

### iOS — Apple Sign In for Unity v1.4.2

| | |
|---|---|
| **Страница релиза** | https://github.com/lupidan/apple-signin-unity/releases/tag/v1.4.2 |
| **Прямая ссылка** | [`AppleSignIn-1.4.2.unitypackage`](https://github.com/lupidan/apple-signin-unity/releases/download/v1.4.2/AppleSignIn-1.4.2.unitypackage) |

1. Импортировать пакет.
2. Пост-процессор плагина автоматически добавит capability **Sign In with Apple** в Xcode-проект.
3. Подключить `IAppleAuthManager` в `UGSAuthService` (см. комментарий `TODO(iOS→UGS)` в исходниках).
4. Передать Services ID в билдер:
```csharp
.WithAuthProviderCredentials(new GameServicesAuthProviderConfig
{
    AppleServicesId = "com.yourcompany.yourgame",
})
```

---

## Интерфейс: `IAuthService`

Доступ через `GameServicesLocator.Services.Auth`.

| Член | Описание |
|------|----------|
| `bool IsSignedIn` | True после успешного `SignInAsync` |
| `string GetPlayerId()` | UUID игрока в UGS; `"unknown"` если не авторизован |
| `string GetPlayerName()` | Никнейм в профиле UGS; пустая строка если не задан |
| `Task<bool> SignInAsync(platform, ct)` | Вход. Платформа может быть переопределена сохранённой сессией. |
| `Task<bool> LinkWithAccountAsync(platform, ct)` | Привязка анонимного аккаунта к Google Play / Apple |
| `void Reset()` | Выход + удаление сохранённого метода входа |
| `NameValidationError? ValidatePlayerName(name)` | Клиентская валидация без сети. `null` = имя корректно. |
| `Task<NameValidationError?> SetPlayerNameAsync(name, ct)` | Валидация + сохранение в UGS. `null` = успех. |

---

## Вход

```csharp
var auth = GameServicesLocator.Services.Auth;

bool ok = await auth.SignInAsync(AuthPlatform.GooglePlayGames, destroyCancellationToken);
if (!ok)
{
    Debug.LogWarning("Вход не выполнен — показываем офлайн UI");
    return;
}
Debug.Log($"Выполнен вход: {auth.GetPlayerId()}");
```

Логика `SignInAsync` (UGS-реализация):
- `ForceAnonymous = true` → всегда анонимно независимо от платформы
- Первый запуск (нет session token) → анонимный вход
- Повторный запуск → возобновление сохранённого метода (анонимный или платформенный)

---

## Привязка платформенного аккаунта

Вызывать **после** анонимного входа — обычно после обучения, когда игрок нажимает «Войти через Google»:

```csharp
bool linked = await auth.LinkWithAccountAsync(AuthPlatform.GooglePlayGames, ct);
if (linked)
    ShowToast("Аккаунт привязан — прогресс сохраняется в облаке!");
```

---

## Никнейм — валидация и установка

### Клиентская предварительная проверка (мгновенно, без сети)

Вызывать при каждом изменении поля ввода:

```csharp
void OnNameInputChanged(string input)
{
    var error = auth.ValidatePlayerName(input);
    errorLabel.text = error switch
    {
        null                                  => "",
        NameValidationError.Empty             => "Введите никнейм",
        NameValidationError.TooShort          => "Минимум 3 символа",
        NameValidationError.TooLong           => "Максимум 50 символов",
        NameValidationError.InvalidCharacter  => "Допустимы: буквы, цифры, пробел, - _ .",
        NameValidationError.Profanity         => "Недопустимое слово",
        _                                     => "Некорректное имя",
    };
    confirmButton.interactable = error == null;
}
```

### Сохранение никнейма (клиент + сервер)

```csharp
async void OnConfirmClicked()
{
    confirmButton.interactable = false;
    spinner.SetActive(true);

    var result = await auth.SetPlayerNameAsync(nameInput.text, destroyCancellationToken);

    spinner.SetActive(false);
    confirmButton.interactable = true;

    if (result == null)
    {
        ShowSuccessPanel();
        return;
    }

    errorLabel.text = result switch
    {
        NameValidationError.NotSignedIn    => "Не выполнен вход. Перезапустите игру.",
        NameValidationError.ServerRejected => "Имя отклонено сервером. Попробуйте другое.",
        NameValidationError.NetworkError   => "Ошибка сети. Проверьте соединение и попробуйте снова.",
        _                                  => "Ошибка валидации. Попробуйте другое имя.",
    };
}
```

---

## Справка по NameValidationError

| Значение | Источник | Смысл |
|----------|----------|-------|
| `Empty` | Клиент | null / пробелы |
| `TooShort` | Клиент | < 3 символов |
| `TooLong` | Клиент | > 50 символов |
| `InvalidCharacter` | Клиент | Символы вне `[A-Za-z0-9 \-_.]` |
| `Profanity` | Клиент | Совпадение с бан-листом `NameValidatorConfig` |
| `NotSignedIn` | Сервер | Вход не выполнен до вызова `SetPlayerNameAsync` |
| `ServerRejected` | Сервер | UGS HTTP 422 — имя нарушает серверные ограничения |
| `NetworkError` | Сервер | Сетевая ошибка или непредвиденное исключение |

---

## Настройка фильтра нецензурных слов

### Вариант А — ScriptableObject (Inspector)

1. **Project → ПКМ → Create** — ScriptableObject антицензора в проекте игры (или передайте `string[]` / `Regex` через `WithProfanityFilter`)
2. Заполнить массив `Banned Words` и/или `Banned Pattern` (строка regex)
3. Перетащить ассет в поле `Profanity Config` компонента `ServicesBootstrap`

```csharp
.WithNameValidator(_profanityConfig?.ToValidatorConfig())
```

### Вариант Б — Инлайн в бутстрапе

```csharp
.WithProfanityFilter("слово1", "слово2")
// или с regex:
.WithProfanityFilter(new Regex(@"шаб\w+", RegexOptions.IgnoreCase))
```

### Вариант В — Полный объект конфигурации

```csharp
.WithNameValidator(new NameValidatorConfig(
    bannedWords: new[] { "слово1", "слово2" },
    bannedPattern: new Regex(@"мат\w+", RegexOptions.IgnoreCase)
))
```

> **Совет:** бан-лист — это **данные игры**, не SDK. Держите его в ScriptableObject или загружайте с сервера (Remote Config) — так его можно обновить без пересборки APK.
