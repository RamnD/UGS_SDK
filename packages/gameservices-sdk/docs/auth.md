# Auth Service

← [Back to README](../README.md) · [Identities / cloud save](auth-identities.md)

---

## Platform plugin prerequisites

The UGS auth layer wraps platform-specific SDKs that are **not in the UPM registry** and must be imported manually.

### Android — Google Play Games Plugin v2.1.0

GPGS auth compiles only when Unity sets `RAMND_HAS_GOOGLE_PLAY_GAMES`. That define comes from asmdef `versionDefines` and **only fires for a real UPM package** (`com.google.play.games`). Dropping the `.unitypackage` under `Assets/` is **not enough** — the SDK then builds the `#else` stubs and Android Link fails with “Google Play Games plugin is missing”.

| | |
|---|---|
| **Preferred install** | UPM git / embedded package (see below) |
| **Release page** | https://github.com/playgameservices/play-games-plugin-for-unity/releases/tag/v2.1.0 |
| **UPM git URL** | `https://github.com/playgameservices/play-games-plugin-for-unity.git?path=com.google.play.games` |

**Install (pick one):**
1. **UPM git** — add the URL above to `Packages/manifest.json`, **or**
2. **Embedded local** — put the plugin at `Packages/com.google.play.games/` (folder with `package.json` name `com.google.play.games`), **or**
3. **Assets import fallback** — import the `.unitypackage`, then manually add scripting define `RAMND_HAS_GOOGLE_PLAY_GAMES` for the **Android** player (Player Settings → Scripting Define Symbols).

Then:
1. **Window → Google Play Games → Setup → Android Setup** — paste your OAuth Web Client ID (Google Cloud Console, type **Web application**).
2. Pass the same ID to the builder:
```csharp
.WithAuthProviderCredentials(new GameServicesAuthProviderConfig
{
    GooglePlayGamesOAuthWebClientId = "YOUR_WEB_CLIENT_ID.apps.googleusercontent.com",
})
```

Call `GooglePlayGamesProfileProvider.WarmUp()` once at Android bootstrap (before the Link button).

If native sign-in UI appears then returns `SignInStatus.Canceled` (~2–4s):

1. Register **both** SHA-1 fingerprints as Android OAuth clients: local/upload keystore (sideload APK) **and** Play Console **App signing key** (Play-installed builds). Sideload APKs use the upload key; Play-installed builds use App Signing.
2. Add the Google account to **Play Console → Play Games Services → Testers**.
3. Play Games Services configuration: **Use next generation IDs = Off**.
4. Prefer a Play **internal testing** install over a USB sideload when verifying production signing.
5. Keep **Application Entry Point = GameActivity** if that is the project default. Switching to Activity is not required for GPGS and can hide the launcher icon (`UnityPlayerActivity` merged as `android:enabled="false"`).

### iOS — Apple Game Center (recommended for games) + optional SIWA

Install Apple plugins as **UPM packages** (tarball / git / `file:`). Same rule as GPGS: asmdef `versionDefines` do not see loose `Assets/` copies.

**Game Center (primary):**
```csharp
.WithAuthProviderCredentials(new GameServicesAuthProviderConfig
{
    RequestAppleGameCenterCredentialsAsync = ct => AppleGameCenterCredentialsProvider.RequestAsTaskAsync(ct),
})
```

Requires Apple.Core + Apple.GameKit (`com.apple.unityplugin.gamekit`; SDK sets `RAMND_HAS_APPLE_GAMEKIT`) and UGS Dashboard → Apple Game Center (Bundle ID). The SDK now includes `AppleGameCenterCredentialsProvider` and `TryGetAuthenticatedDisplayName` / `TryLoadAuthenticatedPhotoAsync` helpers for game-side profile import.

**Sign in with Apple (optional):**
```csharp
.WithAuthProviderCredentials(new GameServicesAuthProviderConfig
{
    AppleServicesId = "com.yourcompany.yourgame",
    RequestAppleIdentityTokenAsync = ct => AppleSignInIdentityTokenProvider.RequestAsync(ct),
})
```

The built-in `AppleSignInIdentityTokenProvider` requires `com.lupidan.apple-signin-unity`. If your project already has another SIWA implementation, you can still pass your own `RequestAppleIdentityTokenAsync`.

### Optional platform define map

| Define | Set when (UPM package / assembly) | Enables |
|--------|-----------------------------------|---------|
| `RAMND_HAS_GOOGLE_PLAY_GAMES` | `com.google.play.games` / `Google.Play.Games` | GPGS SignIn / Link / profile / achievement bridge |
| `RAMND_HAS_APPLE_GAMEKIT` | `com.apple.unityplugin.gamekit` / `Apple.GameKit` | Game Center credential provider + achievement bridge |
| `RAMND_HAS_APPLE_SIGNIN` | `com.lupidan.apple-signin-unity` | Built-in SIWA identity-token provider |
| `RAMND_HAS_GOOGLE_PLAY_APP_UPDATE` | `com.google.play.appupdate` | Play Immediate in-app update adapter |
| `RAMND_HAS_GOOGLE_MOBILE_ADS` | `com.google.ads.mobile` 8.5+ | Google UMP consent gate |

### Built-in auth helpers

The SDK includes a few reusable auth helpers so consuming games do not need to re-copy the same glue:

- `AuthSessionEnsure.EnsureSignedInAsync(context, ct)` — soft anonymous restore when Auth is unexpectedly signed out but the network is still healthy
- `AppleGameCenterCredentialsProvider` — GameKit -> UGS Apple Game Center credentials
- `AppleSignInIdentityTokenProvider` — Sign in with Apple JWT bridge
- `GooglePlayGamesProfileProvider` — post-auth display name / avatar URL reader for GPGS profile import

---

## Interface: `IAuthService`

Exposed via `GameServicesLocator.Services.Auth`.

| Member | Description |
|--------|-------------|
| `bool IsSignedIn` | True after successful `SignInAsync` |
| `string GetPlayerId()` | UGS player UUID; `"unknown"` if not signed in |
| `string GetPlayerName()` | Display name in UGS profile; empty string if unset |
| `Task<bool> SignInAsync(platform, ct)` | Signs in. Platform may be overridden by saved session. |
| `Task<AccountLinkResult> LinkWithAccountAsync(platform, ct)` | Links anonymous → platform, or recovers into existing player if already linked |
| `Task<bool> UnlinkWithAccountAsync(platform, ct)` | Unlinks an external identity from the current player |
| `bool IsIdentityLinked(platform)` | Whether PlayerInfo has that external type id |
| `IReadOnlyList<string> GetLinkedIdentityTypeIds()` | All linked external type ids |
| `Task<bool> DeleteAccountAsync(ct)` | Permanently deletes the UGS Authentication player (App Store 5.1.1) |
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
AccountLinkResult result = await auth.LinkWithAccountAsync(AuthPlatform.GooglePlayGames, ct);
switch (result)
{
    case AccountLinkResult.Linked:
        ShowToast("Account linked — progress saved to cloud!");
        break;
    case AccountLinkResult.SignedIntoExisting:
        // External ID was already on another UGS player (typical after reinstall).
        // Reload Cloud Save / Economy for the restored PlayerId, then resolve SaveConflict if any.
        await ReloadProgressAfterAccountSwitchAsync();
        break;
    default:
        ShowToast("Link failed");
        break;
}
```

### Reinstall / already linked

If Game Center / Google Play is already tied to a previous UGS `PlayerId`, `LinkWith*` fails with `AccountAlreadyLinked`. The SDK then:

1. Checks whether the **current** anonymous player looks empty:
   online Cloud Save has no player keys (ignoring `__ts`), **and** Economy balances are all 0, **and** Player Inventory is empty
2. **Empty** → `DeleteAccountAsync` (avoid orphan) · **Non-empty / offline / check failed** → `SignOut` only (server data preserved; cannot delete after switch)
3. Requests **fresh** platform credentials
4. Calls `SignInWith*` into the existing linked player
5. Returns `AccountLinkResult.SignedIntoExisting`

Local game saves are **not** wiped — the game should show a SaveConflict UI (keep local vs apply cloud). Do **not** use UGS `ForceLink`.

**Two supported patterns:**

| Flow | Behaviour |
|------|-----------|
| Anonymous links an already-used social ID | Leave current session (Delete if empty, else SignOut) → SignIn existing → SaveConflict UI |
| Profile «Delete account» | Wipe game data while signed in → `DeleteAccountAsync` → reload / new anonymous |

---

## Delete account (App Store 5.1.1)

`Reset()` only signs out. Platform links (Game Center / Google) stay on the UGS player — after reinstall, Link hits `AccountAlreadyLinked`.

For a real account deletion:

1. While still signed in, wipe Cloud Save / Economy / local progress in the game
   (`ClearLocalCache` on Economy, CloudSave, Items, Consumables, Achievements)
2. Call `DeleteAccountAsync` — wraps `AuthenticationService.Instance.DeleteAccountAsync()`, clears `last_auth_method`
3. Cold-start / reload so bootstrap creates a fresh anonymous session

```csharp
await WipeGameDataWhileSignedInAsync(ct); // Cloud Save empty push, Economy zero, local prefs
bool ok = await auth.DeleteAccountAsync(ct);
// then reload bootstrap / SignInAsync → new anonymous player
```

`DeleteAccountAsync` does **not** wipe Cloud Save or Economy by itself.

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
