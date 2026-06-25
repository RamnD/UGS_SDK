# Auth Service

← [Back to README](../README.md)

---

## Platform plugin prerequisites

The UGS auth layer wraps platform-specific SDKs that are **not in the UPM registry** and must be imported manually.

### Android — Google Play Games Plugin v2.1.0

| | |
|---|---|
| **Release page** | https://github.com/playgameservices/play-games-plugin-for-unity/releases/tag/v2.1.0 |
| **Direct download** | [`GooglePlayGamesPlugin-2.1.0.unitypackage`](https://github.com/playgameservices/play-games-plugin-for-unity/releases/download/v2.1.0/GooglePlayGamesPlugin-2.1.0.unitypackage) |

1. Import the package via **Assets → Import Package → Custom Package**.
2. **Window → Google Play Games → Setup → Android Setup** — paste your OAuth Web Client ID (from Google Cloud Console, type **Web application**).
3. Pass the same ID to the builder:
```csharp
.WithAuthProviderCredentials(new GameServicesAuthProviderConfig
{
    GooglePlayGamesOAuthWebClientId = "YOUR_WEB_CLIENT_ID.apps.googleusercontent.com",
})
```

### iOS — Apple Sign In for Unity v1.4.2

| | |
|---|---|
| **Release page** | https://github.com/lupidan/apple-signin-unity/releases/tag/v1.4.2 |
| **Direct download** | [`AppleSignIn-1.4.2.unitypackage`](https://github.com/lupidan/apple-signin-unity/releases/download/v1.4.2/AppleSignIn-1.4.2.unitypackage) |

1. Import the package.
2. The plugin's post-process step adds the **Sign In with Apple** Xcode capability automatically.
3. Wire `IAppleAuthManager` into `UGSAuthService` (see `TODO(iOS→UGS)` comment in the source).
4. Pass your Services ID to the builder:
```csharp
.WithAuthProviderCredentials(new GameServicesAuthProviderConfig
{
    AppleServicesId = "com.yourcompany.yourgame",
})
```

---

## Interface: `IAuthService`

Exposed via `GameServicesLocator.Services.Auth`.

| Member | Description |
|--------|-------------|
| `bool IsSignedIn` | True after successful `SignInAsync` |
| `string GetPlayerId()` | UGS player UUID; `"unknown"` if not signed in |
| `string GetPlayerName()` | Display name in UGS profile; empty string if unset |
| `Task<bool> SignInAsync(platform, ct)` | Signs in. Platform may be overridden by saved session. |
| `Task<bool> LinkWithAccountAsync(platform, ct)` | Links anonymous account to Google Play / Apple |
| `void Reset()` | Sign out + delete saved auth method |
| `NameValidationError? ValidatePlayerName(name)` | Client-side only; no network. `null` = valid. |
| `Task<NameValidationError?> SetPlayerNameAsync(name, ct)` | Validates + saves to UGS. `null` = success. |

---

## Sign in

```csharp
var auth = GameServicesLocator.Services.Auth;

bool ok = await auth.SignInAsync(AuthPlatform.GooglePlayGames, destroyCancellationToken);
if (!ok)
{
    Debug.LogWarning("Sign-in failed — showing offline UI");
    return;
}
Debug.Log($"Signed in as {auth.GetPlayerId()}");
```

`SignInAsync` behaviour (UGS implementation):
- `ForceAnonymous = true` → always anonymous regardless of platform
- First-ever run (no session token) → anonymous sign-in
- Subsequent runs → resumes the saved method (anonymous or linked platform)

---

## Linking a platform account

Call **after** the player is already signed in anonymously (e.g. after tutorial):

```csharp
bool linked = await auth.LinkWithAccountAsync(AuthPlatform.GooglePlayGames, ct);
if (linked)
    ShowToast("Account linked — progress saved to cloud!");
```

---

## Player name — validation + setting

### Client-side pre-validation (instant, no network)

Use before showing an error while the player types:

```csharp
void OnNameInputChanged(string input)
{
    var error = auth.ValidatePlayerName(input);
    errorLabel.text = error switch
    {
        null                           => "",
        NameValidationError.Empty      => "Enter a nickname",
        NameValidationError.TooShort   => "At least 3 characters",
        NameValidationError.TooLong    => "50 characters max",
        NameValidationError.InvalidCharacter => "Letters, digits, space, - _ . only",
        NameValidationError.Profanity  => "That word is not allowed",
        _                              => "Invalid name",
    };
    confirmButton.interactable = error == null;
}
```

### Setting the name (client + server)

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
        NameValidationError.NotSignedIn    => "Not signed in. Please restart the game.",
        NameValidationError.ServerRejected => "Name not allowed by server. Try another.",
        NameValidationError.NetworkError   => "Network error. Check connection and retry.",
        _                                  => "Validation failed. Try a different name.",
    };
}
```

---

## NameValidationError enum reference

| Value | Source | Meaning |
|-------|--------|---------|
| `Empty` | Client | Null / whitespace |
| `TooShort` | Client | < 3 chars |
| `TooLong` | Client | > 50 chars |
| `InvalidCharacter` | Client | Chars outside `[A-Za-z0-9 \-_.]` |
| `Profanity` | Client | Matched `NameValidatorConfig` banned list/pattern |
| `NotSignedIn` | Server | Auth not completed before calling `SetPlayerNameAsync` |
| `ServerRejected` | Server | UGS HTTP 422 — name violates server-side rules |
| `NetworkError` | Server | Network failure or unexpected exception |

---

## Profanity filter configuration

### Option A — ScriptableObject (Inspector)

1. **Project window → right-click → Create** your game's profanity ScriptableObject (or pass `string[]` / `Regex` via `WithProfanityFilter`)
2. Fill `Banned Words` array and/or `Banned Pattern` (regex string)
3. Drag the asset into `ServicesBootstrap._profanityConfig`

```csharp
.WithNameValidator(_profanityConfig?.ToValidatorConfig())
```

### Option B — Inline in bootstrap

```csharp
.WithProfanityFilter("badword", "otherword")
// or with regex:
.WithProfanityFilter(new Regex(@"bad\w+", RegexOptions.IgnoreCase))
```

### Option C — Full config object

```csharp
.WithNameValidator(new NameValidatorConfig(
    bannedWords: new[] { "foo", "bar" },
    bannedPattern: new Regex(@"f[o0]+", RegexOptions.IgnoreCase)
))
```
