// UGSAuthService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Economy;
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

        // Persist the same NFKC form used for validation / ban checks.
        string normalized = name.Normalize(NormalizationForm.FormKC);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AuthenticationService.Instance.UpdatePlayerNameAsync(normalized);
            cancellationToken.ThrowIfCancellationRequested();
            Debug.Log("[Auth] PlayerName updated.");
            return null;
        }
        catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
        {
            Debug.LogWarning($"[Auth] Server rejected player name: {e.Message}");
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

        // NFKC collapses compatibility forms; length/charset checks use the normalized form.
        string normalized = name.Normalize(NormalizationForm.FormKC);

        if (normalized.Length < 3)
            return NameValidationError.TooShort;

        if (normalized.Length > 50)
            return NameValidationError.TooLong;

        foreach (char c in normalized)
            if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_' && c != '.')
                return NameValidationError.InvalidCharacter;

        // Ban list: compare against a Latin-lookalike fold so Cyrillic а/е/о etc. cannot bypass ASCII bans.
        string folded = FoldHomoglyphsForBanCheck(normalized).ToLowerInvariant();
        foreach (var word in _validatorConfig.BannedWords)
        {
            if (string.IsNullOrEmpty(word))
                continue;
            string foldedWord = FoldHomoglyphsForBanCheck(word.Normalize(NormalizationForm.FormKC))
                .ToLowerInvariant();
            if (foldedWord.Length > 0 && folded.Contains(foldedWord))
                return NameValidationError.Profanity;
        }

        if (_validatorConfig.BannedPattern != null)
        {
            try
            {
                if (_validatorConfig.BannedPattern.IsMatch(normalized)
                    || _validatorConfig.BannedPattern.IsMatch(folded))
                    return NameValidationError.Profanity;
            }
            catch (RegexMatchTimeoutException)
            {
                Debug.LogWarning("[Auth] BannedPattern match timed out — treating as invalid.");
                return NameValidationError.Profanity;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps common Cyrillic/Greek lookalikes to Latin letters for ban-list matching only.
    /// Does not change the name sent to the server.
    /// </summary>
    static string FoldHomoglyphsForBanCheck(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(c switch
            {
                // Cyrillic → Latin lookalikes
                '\u0430' or '\u0410' => 'a', // а А
                '\u0435' or '\u0415' => 'e', // е Е
                '\u043E' or '\u041E' => 'o', // о О
                '\u0440' or '\u0420' => 'p', // р Р
                '\u0441' or '\u0421' => 'c', // с С
                '\u0443' or '\u0423' => 'y', // у У
                '\u0445' or '\u0425' => 'x', // х Х
                '\u0456' or '\u0406' => 'i', // і І
                '\u04CF' or '\u04C0' => 'i', // ӏ Ӏ
                // Greek → Latin lookalikes
                '\u03B1' or '\u0391' => 'a', // α Α
                '\u03B5' or '\u0395' => 'e', // ε Ε
                '\u03BF' or '\u039F' => 'o', // ο Ο
                '\u03C1' or '\u03A1' => 'p', // ρ Ρ
                '\u03C5' or '\u03A5' => 'y', // υ Υ
                '\u03C7' or '\u03A7' => 'x', // χ Χ
                '\u03B9' or '\u0399' => 'i', // ι Ι
                _ => c
            });
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public async Task<bool> SignInAsync(AuthPlatform platform, CancellationToken cancellationToken = default)
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UGSUnityServicesInitializer.EnsureInitializedAsync(cancellationToken);
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
                "leaving current session (delete only if empty) and signing into existing account.");
            return await SignIntoExistingAfterAlreadyLinkedAsync(platform, cancellationToken);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Auth] Account link failed ({platform}): {e.Message}");
            return AccountLinkResult.Failed;
        }
    }

    /// <summary>
    /// After <see cref="AuthenticationErrorCodes.AccountAlreadyLinked"/>: leave the current
    /// anonymous session, then SignIn with the platform identity into the existing linked account.
    /// <list type="bullet">
    /// <item>Empty Cloud Save (or offline-unverifiable treated as non-empty) → <c>DeleteAccount</c> to avoid orphans.</item>
    /// <item>Non-empty → <c>SignOut</c> only — server data for the anonymous player is preserved.</item>
    /// </list>
    /// Does not touch game local saves — the caller resolves SaveConflict in UI.
    /// Does not use ForceLink — that would steal the identity onto the wrong player.
    /// After switch, the previous anonymous player can no longer be deleted from the client.
    /// </summary>
    private async Task<AccountLinkResult> SignIntoExistingAfterAlreadyLinkedAsync(
        AuthPlatform platform,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSignedIn)
            {
                await LeaveCurrentSessionForRecoverAsync(cancellationToken);
                PlayerPrefs.DeleteKey(LastAuthMethodKey);
                PlayerPrefs.Save();
            }

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

    /// <summary>
    /// Deletes only when Cloud Save looks empty; otherwise SignOut so non-empty anonymous progress is not wiped.
    /// Fail-safe on network/check errors: SignOut (never Delete).
    /// </summary>
    private async Task LeaveCurrentSessionForRecoverAsync(CancellationToken cancellationToken)
    {
        string orphanId = GetPlayerId();
        bool deleteOrphan = await IsCurrentPlayerEffectivelyEmptyAsync(cancellationToken);

        if (deleteOrphan)
        {
            try
            {
                await AuthenticationService.Instance.DeleteAccountAsync();
                Debug.Log($"[Auth] Deleted empty anonymous PlayerId={orphanId} before recover SignIn.");
                return;
            }
            catch (Exception deleteEx)
            {
                Debug.LogWarning(
                    $"[Auth] Could not delete empty anonymous ({deleteEx.Message}) — SignOut fallback.");
            }
        }
        else
        {
            Debug.Log(
                $"[Auth] Signed out non-empty anonymous PlayerId={orphanId} before recover " +
                "(server data preserved; cannot delete after switch).");
        }

        if (IsSignedIn)
            AuthenticationService.Instance.SignOut(clearCredentials: true);
    }

    /// <summary>
    /// True when online Cloud Save has no player keys (or only ignorable internals)
    /// <b>and</b> Economy balances/inventory are empty. Offline / load failure → false (do not Delete).
    /// </summary>
    private static async Task<bool> IsCurrentPlayerEffectivelyEmptyAsync(CancellationToken cancellationToken)
    {
        if (!NetworkStatus.IsOnline)
        {
            Debug.LogWarning("[Auth] Cannot verify empty orphan offline — SignOut instead of Delete.");
            return false;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await CloudSaveService.Instance.Data.Player.LoadAllAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (items != null)
            {
                foreach (var key in items.Keys)
                {
                    if (string.IsNullOrEmpty(key))
                        continue;
                    // Reserved / internal keys used by this SDK's Cloud Save helpers.
                    if (string.Equals(key, "__ts", StringComparison.Ordinal))
                        continue;
                    return false;
                }
            }

            // Cloud Save empty is not enough — IAP/currency may exist only in Economy.
            cancellationToken.ThrowIfCancellationRequested();
            var balances = await EconomyService.Instance.PlayerBalances.GetBalancesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (balances?.Balances != null)
            {
                for (int i = 0; i < balances.Balances.Count; i++)
                {
                    if (balances.Balances[i] != null && balances.Balances[i].Balance > 0)
                        return false;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var inventory = await EconomyService.Instance.PlayerInventory.GetInventoryAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (inventory?.PlayersInventoryItems != null && inventory.PlayersInventoryItems.Count > 0)
                return false;

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Auth] Empty-check failed ({ex.Message}) — SignOut instead of Delete.");
            return false;
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
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration ctr = default;
        if (cancellationToken.CanBeCanceled)
        {
            ctr = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

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

        return AwaitAuthCodeAndDisposeAsync(tcs.Task, ctr);
    }

    static async Task<string> AwaitAuthCodeAndDisposeAsync(
        Task<string> task,
        CancellationTokenRegistration ctr)
    {
        try
        {
            return await task;
        }
        finally
        {
            ctr.Dispose();
        }
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
