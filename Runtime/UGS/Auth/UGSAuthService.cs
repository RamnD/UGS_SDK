// UGSAuthService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using UnityEngine;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

/// <summary>
/// <see cref="IAuthService"/> implementation via Unity Gaming Services Authentication SDK.
/// <para>
/// Sign-in strategy:
/// <list type="bullet">
/// <item>Anonymous (or forceAnonymous) — always anonymous sign-in, prefs untouched.</item>
/// <item>First visit (no session token) — anonymous sign-in, save "Anonymous".</item>
/// <item>Return visit — use saved method from PlayerPrefs, ignore platform.</item>
/// </list>
/// </para>
/// </summary>
public class UGSAuthService : IAuthService
{
    private const string LastAuthMethodKey = "last_auth_method";

    private readonly NameValidatorConfig            _validatorConfig;
    private readonly GameServicesAuthProviderConfig _providerConfig;

    /// <param name="config">
    /// Profanity-filter configuration. Passed from <see cref="UGSServicesBuilder"/>.
    /// Null is equivalent to <see cref="NameValidatorConfig.Empty"/>.
    /// </param>
    /// <param name="providerConfig">Optional GPGS / Apple keys (see <see cref="GameServicesAuthProviderConfig"/>).</param>
    public UGSAuthService(
        NameValidatorConfig            config         = null,
        GameServicesAuthProviderConfig providerConfig = null)
    {
        _validatorConfig = config ?? NameValidatorConfig.Empty;
        _providerConfig  = providerConfig ?? GameServicesAuthProviderConfig.Empty;
    }

    /// <inheritdoc/>
    public bool IsSignedIn => AuthenticationService.Instance.IsSignedIn;

    /// <inheritdoc/>
    public string GetPlayerId() =>
        IsSignedIn ? AuthenticationService.Instance.PlayerId : "unknown";

    /// <inheritdoc/>
    public string GetPlayerName() =>
        IsSignedIn ? (AuthenticationService.Instance.PlayerName ?? "") : "";

    /// <inheritdoc/>
    public async Task<NameValidationError?> SetPlayerNameAsync(string name,
        CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn)
        {
            Debug.LogError("[Auth] SetPlayerNameAsync: not signed in.");
            return NameValidationError.NotSignedIn;
        }

        var clientError = ValidatePlayerName(name);
        if (clientError != null)
        {
            Debug.LogWarning($"[Auth] SetPlayerNameAsync: client validation failed — {clientError}");
            return clientError;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AuthenticationService.Instance.UpdatePlayerNameAsync(name);
            cancellationToken.ThrowIfCancellationRequested();
            Debug.Log($"[Auth] PlayerName updated: \"{GetPlayerName()}\"");
            return null;
        }
        catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
        {
            Debug.LogWarning($"[Auth] Server rejected name \"{name}\": {e.Message}");
            return NameValidationError.ServerRejected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] UpdatePlayerName failed: {e.Message}");
            return NameValidationError.NetworkError;
        }
    }

    /// <inheritdoc/>
    public NameValidationError? ValidatePlayerName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return NameValidationError.Empty;

        if (name.Length < 3)
            return NameValidationError.TooShort;

        if (name.Length > 50)
            return NameValidationError.TooLong;

        foreach (char c in name)
            if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_' && c != '.')
                return NameValidationError.InvalidCharacter;

        string lower = name.ToLowerInvariant();
        foreach (var word in _validatorConfig.BannedWords)
            if (lower.Contains(word.ToLowerInvariant()))
                return NameValidationError.Profanity;

        if (_validatorConfig.BannedPattern != null &&
            _validatorConfig.BannedPattern.IsMatch(name))
            return NameValidationError.Profanity;

        return null;
    }

    /// <inheritdoc/>
    public async Task<bool> SignInAsync(AuthPlatform platform, CancellationToken cancellationToken = default)
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UnityServices.InitializeAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (IsSignedIn) return true;

            if (platform == AuthPlatform.Anonymous)
            {
                Debug.Log("[Auth] Forced anonymous sign-in.");
                cancellationToken.ThrowIfCancellationRequested();
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            else if (!AuthenticationService.Instance.SessionTokenExists)
            {
                Debug.Log("[Auth] First visit — anonymous sign-in.");
                cancellationToken.ThrowIfCancellationRequested();
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                SaveLastMethod(AuthPlatform.Anonymous);
            }
            else
            {
                AuthPlatform lastMethod = LoadLastMethod();
                Debug.Log($"[Auth] Returning visit — signing in via: {lastMethod}.");
                cancellationToken.ThrowIfCancellationRequested();
                await SignInWithMethodAsync(lastMethod, cancellationToken);
            }

            Debug.Log($"[Auth] Success. PlayerId={GetPlayerId()}");
            return true;
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[Auth] Sign-in cancelled.");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] Sign-in failed: {e.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<AccountLinkResult> LinkWithAccountAsync(AuthPlatform platform,
        CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn)
        {
            Debug.LogError("[Auth] Cannot link account — not signed in.");
            return AccountLinkResult.NotSignedIn;
        }

        try
        {
            switch (platform)
            {
                case AuthPlatform.GooglePlayGames:
#if UNITY_ANDROID
                    if (string.IsNullOrWhiteSpace(_providerConfig.GooglePlayGamesOAuthWebClientId))
                    {
                        // TODO(GPGS→UGS): use GooglePlayGamesOAuthWebClientId when the build actually depends on passing the key through the SDK (GPGS often sets the client via Android resources today).
                        Debug.LogWarning(
                            "[Auth] TODO(GPGS→UGS): GooglePlayGamesOAuthWebClientId not passed via WithAuthProviderCredentials; add Web Client Id from GCP / game config if linking fails.");
                    }
#endif
                    cancellationToken.ThrowIfCancellationRequested();
                    await LinkWithGooglePlayGamesAsync(cancellationToken);
                    break;

                case AuthPlatform.Apple:
                    cancellationToken.ThrowIfCancellationRequested();
                    await LinkWithAppleAsync(cancellationToken);
                    break;

                case AuthPlatform.AppleGameCenter:
                    cancellationToken.ThrowIfCancellationRequested();
                    await LinkWithAppleGameCenterAsync(cancellationToken);
                    break;

                default:
                    Debug.LogError("[Auth] Anonymous cannot be used as a link target.");
                    return AccountLinkResult.Failed;
            }

            SaveLastMethod(platform);
            Debug.Log($"[Auth] Account linked: {platform}. PlayerId={GetPlayerId()}");
            return AccountLinkResult.Linked;
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[Auth] Account link cancelled.");
            return AccountLinkResult.Cancelled;
        }
        catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
        {
            Debug.LogWarning(
                $"[Auth] External ID already linked to another player ({platform}) — " +
                "signing into existing account (reinstall recover).");
            return await SignIntoExistingAfterAlreadyLinkedAsync(platform, cancellationToken);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] Account link failed ({platform}): {e.Message}");
            return AccountLinkResult.Failed;
        }
    }

    /// <summary>
    /// After <see cref="AuthenticationErrorCodes.AccountAlreadyLinked"/>: drop the current
    /// (usually fresh anonymous) session and SignIn with the same platform identity.
    /// Does not use ForceLink — that would steal the identity onto the wrong player.
    /// </summary>
    private async Task<AccountLinkResult> SignIntoExistingAfterAlreadyLinkedAsync(
        AuthPlatform platform,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSignedIn)
                AuthenticationService.Instance.SignOut(clearCredentials: true);

            cancellationToken.ThrowIfCancellationRequested();
            await SignInWithMethodAsync(platform, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSignedIn)
            {
                Debug.LogError($"[Auth] Recover SignIn failed — still not signed in ({platform}).");
                return AccountLinkResult.Failed;
            }

            SaveLastMethod(platform);
            Debug.Log(
                $"[Auth] Signed into existing account via {platform}. PlayerId={GetPlayerId()}");
            return AccountLinkResult.SignedIntoExisting;
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[Auth] Recover SignIn cancelled.");
            return AccountLinkResult.Cancelled;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] Recover SignIn failed ({platform}): {e.Message}");
            return AccountLinkResult.Failed;
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        if (IsSignedIn)
            AuthenticationService.Instance.SignOut(clearCredentials: true);

        PlayerPrefs.DeleteKey(LastAuthMethodKey);
        PlayerPrefs.Save();
        Debug.Log("[Auth] Session cleared. Next sign-in will create a new anonymous session.");
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn)
        {
            PlayerPrefs.DeleteKey(LastAuthMethodKey);
            PlayerPrefs.Save();
            Debug.LogWarning("[Auth] DeleteAccountAsync: not signed in — cleared local auth prefs only.");
            return true;
        }

        string playerId = GetPlayerId();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AuthenticationService.Instance.DeleteAccountAsync();
            cancellationToken.ThrowIfCancellationRequested();

            PlayerPrefs.DeleteKey(LastAuthMethodKey);
            PlayerPrefs.Save();
            Debug.Log($"[Auth] Account deleted. Former PlayerId={playerId}");
            return true;
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("[Auth] DeleteAccountAsync cancelled.");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] DeleteAccountAsync failed: {e.Message}");
            return false;
        }
    }

    private static void SaveLastMethod(AuthPlatform method)
    {
        PlayerPrefs.SetString(LastAuthMethodKey, method.ToString());
        PlayerPrefs.Save();
    }

    private static AuthPlatform LoadLastMethod()
    {
        string saved = PlayerPrefs.GetString(LastAuthMethodKey, AuthPlatform.Anonymous.ToString());
        return Enum.TryParse(saved, out AuthPlatform result) ? result : AuthPlatform.Anonymous;
    }

    private Task SignInWithMethodAsync(AuthPlatform method, CancellationToken cancellationToken) =>
        method switch
        {
            AuthPlatform.GooglePlayGames  => SignInWithGooglePlayGamesAsync(cancellationToken),
            AuthPlatform.Apple            => SignInWithAppleAsync(cancellationToken),
            AuthPlatform.AppleGameCenter  => SignInWithAppleGameCenterAsync(cancellationToken),
            _                             => AuthenticationService.Instance.SignInAnonymouslyAsync()
        };

#if UNITY_ANDROID
    private async Task SignInWithGooglePlayGamesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_providerConfig.GooglePlayGamesOAuthWebClientId))
        {
            Debug.LogWarning(
                "[Auth] TODO(GPGS→UGS): GooglePlayGamesOAuthWebClientId not set; pass WithAuthProviderCredentials if auth fails.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string serverAuthCode = await GetGoogleServerAuthCodeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await AuthenticationService.Instance.SignInWithGooglePlayGamesAsync(serverAuthCode);
    }

    private async Task LinkWithGooglePlayGamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string serverAuthCode = await GetGoogleServerAuthCodeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await AuthenticationService.Instance.LinkWithGooglePlayGamesAsync(serverAuthCode);
    }

    private Task<string> GetGoogleServerAuthCodeAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<string>();

        // Activate so PlayGamesPlatform.Instance is the Social implementation.
        PlayGamesPlatform.Activate();

        void OnAuthComplete(SignInStatus status)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            if (status != SignInStatus.Success)
            {
                Debug.LogError($"[Auth] Google Play Games sign-in failed: {status}");
                tcs.TrySetException(new Exception($"Google Play Games sign-in failed: {status}"));
                return;
            }

            Debug.Log("[Auth] Google Play Games authenticated — requesting server auth code.");
            PlayGamesPlatform.Instance.RequestServerSideAccess(forceRefreshToken: false, authCode =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                if (string.IsNullOrEmpty(authCode))
                {
                    tcs.TrySetException(new Exception(
                        "Google Play Games: RequestServerSideAccess returned empty. " +
                        "Check Web Client ID in GPGS Setup + Android OAuth client SHA-1 in Google Cloud."));
                    return;
                }

                tcs.TrySetResult(authCode);
            });
        }

        // Authenticate() = silent check only (no UI). If the startup auto-prompt was
        // dismissed / failed, Link would appear to do nothing — use ManuallyAuthenticate
        // which shows the Google Play Games sign-in sheet.
        if (PlayGamesPlatform.Instance.IsAuthenticated())
        {
            Debug.Log("[Auth] Google Play Games already authenticated.");
            OnAuthComplete(SignInStatus.Success);
        }
        else
        {
            Debug.Log("[Auth] Google Play Games: showing manual sign-in UI.");
            PlayGamesPlatform.Instance.ManuallyAuthenticate(OnAuthComplete);
        }

        return tcs.Task;
    }
#else
    private Task SignInWithGooglePlayGamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Google Play Games is only available on Android.");
    }

    private Task LinkWithGooglePlayGamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Google Play Games is only available on Android.");
    }
#endif

#if UNITY_IOS
    private async Task SignInWithAppleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_providerConfig.AppleServicesId))
        {
            Debug.LogWarning(
                "[Auth] AppleServicesId is empty — ensure UGS Dashboard Apple provider + game config are set.");
        }

        string identityToken = await RequestAppleIdentityTokenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await AuthenticationService.Instance.SignInWithAppleAsync(identityToken);
    }

    private async Task LinkWithAppleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_providerConfig.AppleServicesId))
        {
            Debug.LogWarning(
                "[Auth] AppleServicesId is empty — ensure UGS Dashboard Apple provider + game config are set.");
        }

        string identityToken = await RequestAppleIdentityTokenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await AuthenticationService.Instance.LinkWithAppleAsync(identityToken);
    }

    private async Task SignInWithAppleGameCenterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleGameCenterCredentials credentials = await RequestAppleGameCenterCredentialsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await AuthenticationService.Instance.SignInWithAppleGameCenterAsync(
            credentials.Signature,
            credentials.TeamPlayerId,
            credentials.PublicKeyUrl,
            credentials.Salt,
            credentials.Timestamp);
    }

    private async Task LinkWithAppleGameCenterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleGameCenterCredentials credentials = await RequestAppleGameCenterCredentialsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await AuthenticationService.Instance.LinkWithAppleGameCenterAsync(
            credentials.Signature,
            credentials.TeamPlayerId,
            credentials.PublicKeyUrl,
            credentials.Salt,
            credentials.Timestamp);
    }

    private async Task<string> RequestAppleIdentityTokenAsync(CancellationToken cancellationToken)
    {
        if (_providerConfig.RequestAppleIdentityTokenAsync == null)
        {
            throw new InvalidOperationException(
                "Apple Sign-In: RequestAppleIdentityTokenAsync is not set. " +
                "Wire the native Apple plugin via GameServicesAuthProviderConfig.");
        }

        string identityToken = await _providerConfig.RequestAppleIdentityTokenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(identityToken))
            throw new InvalidOperationException("Apple Sign-In: identity token is empty.");

        return identityToken;
    }

    private async Task<AppleGameCenterCredentials> RequestAppleGameCenterCredentialsAsync(
        CancellationToken cancellationToken)
    {
        if (_providerConfig.RequestAppleGameCenterCredentialsAsync == null)
        {
            throw new InvalidOperationException(
                "Apple Game Center: RequestAppleGameCenterCredentialsAsync is not set. " +
                "Install Apple GameKit and wire AppleGameCenterCredentialsProvider.");
        }

        AppleGameCenterCredentials credentials =
            await _providerConfig.RequestAppleGameCenterCredentialsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (credentials == null || !credentials.IsValid)
            throw new InvalidOperationException("Apple Game Center: credentials are missing or invalid.");

        return credentials;
    }
#else
    private Task SignInWithAppleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Apple Sign-In is only available on iOS.");
    }

    private Task LinkWithAppleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Apple Sign-In is only available on iOS.");
    }

    private Task SignInWithAppleGameCenterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Apple Game Center is only available on iOS.");
    }

    private Task LinkWithAppleGameCenterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException("Apple Game Center is only available on iOS.");
    }
#endif
}
