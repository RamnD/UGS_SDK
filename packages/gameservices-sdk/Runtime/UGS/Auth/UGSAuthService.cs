// UGSAuthService.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Economy;
using UnityEngine;
#if UNITY_ANDROID && RAMND_HAS_GOOGLE_PLAY_GAMES
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
    private const int RecoverEmptyCheckTimeoutMs = 8000;

    private readonly NameValidatorConfig            _validatorConfig;
    private readonly GameServicesAuthProviderConfig _providerConfig;

#if UNITY_ANDROID
    /// <summary>GPGS auth code from the Link attempt — reused for recover SignIn (avoid second native prompt).</summary>
    string _recoverGooglePlayAuthCode;
#endif
    string _recoverAppleIdentityToken;
    string _recoverGoogleIdToken;
    string _recoverFacebookAccessToken;
    string _recoverOpenIdConnectIdToken;
#if UNITY_IOS
    AppleGameCenterCredentials _recoverGameCenterCredentials;
#endif

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
            AppLog.Error("Auth", "SetPlayerNameAsync: not signed in.");
            return NameValidationError.NotSignedIn;
        }

        var clientError = ValidatePlayerName(name);
        if (clientError != null)
        {
            AppLog.Warn("Auth", $"SetPlayerNameAsync: client validation failed — {clientError}");
            return clientError;
        }

        // Persist the same NFKC form used for validation / ban checks.
        string normalized = name.Normalize(NormalizationForm.FormKC);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await NetworkRequest.WithTimeout(
                AuthenticationService.Instance.UpdatePlayerNameAsync(normalized),
                cancellationToken,
                NetworkRequest.AuthTimeoutMs);
            cancellationToken.ThrowIfCancellationRequested();
            NetworkStatus.ReportSuccess();
            AppLog.Info("Auth", "PlayerName updated.");
            return null;
        }
        catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
        {
            AppLog.Warn("Auth", $"Server rejected player name: {e.Message}");
            return NameValidationError.ServerRejected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            if (IsTransportFailure(e))
                NetworkStatus.ReportFailure();
            AppLog.Error("Auth", $"UpdatePlayerName failed: {e.Message}");
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
                AppLog.Warn("Auth", "BannedPattern match timed out — treating as invalid.");
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
                AppLog.Info("Auth", "Forced anonymous sign-in.");
                cancellationToken.ThrowIfCancellationRequested();
                await NetworkRequest.WithTimeout(
                    AuthenticationService.Instance.SignInAnonymouslyAsync(),
                    cancellationToken,
                    NetworkRequest.AuthTimeoutMs);
            }
            else if (!AuthenticationService.Instance.SessionTokenExists)
            {
                AppLog.Info("Auth", "First visit — anonymous sign-in.");
                cancellationToken.ThrowIfCancellationRequested();
                await NetworkRequest.WithTimeout(
                    AuthenticationService.Instance.SignInAnonymouslyAsync(),
                    cancellationToken,
                    NetworkRequest.AuthTimeoutMs);
                SaveLastMethod(AuthPlatform.Anonymous);
            }
            else
            {
                AuthPlatform lastMethod = LoadLastMethod();
                AppLog.Info("Auth", $"Returning visit — signing in via: {lastMethod}.");
                cancellationToken.ThrowIfCancellationRequested();
                await SignInWithMethodAsync(lastMethod, cancellationToken);
            }

            NetworkStatus.ReportSuccess();
            AppLog.Info("Auth", $"Success. PlayerId={GetPlayerId()}");
            return true;
        }
        catch (OperationCanceledException)
        {
            AppLog.Warn("Auth", "Sign-in cancelled.");
            return false;
        }
        catch (Exception e)
        {
            if (IsTransportFailure(e))
                NetworkStatus.ReportFailure();
            AppLog.Error("Auth", $"Sign-in failed: {e.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<AccountLinkResult> LinkWithAccountAsync(AuthPlatform platform,
        CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn)
        {
            AppLog.Error("Auth", "Cannot link account — not signed in.");
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
                        AppLog.Warn("Auth", "TODO(GPGS→UGS): GooglePlayGamesOAuthWebClientId not passed via WithAuthProviderCredentials; add Web Client Id from GCP / game config if linking fails.");
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

                case AuthPlatform.Google:
                    cancellationToken.ThrowIfCancellationRequested();
                    await LinkWithGoogleOpenIdAsync(cancellationToken);
                    break;

                case AuthPlatform.Facebook:
                    cancellationToken.ThrowIfCancellationRequested();
                    await LinkWithFacebookAsync(cancellationToken);
                    break;

                case AuthPlatform.OpenIdConnect:
                    cancellationToken.ThrowIfCancellationRequested();
                    await LinkWithOpenIdConnectAsync(cancellationToken);
                    break;

                default:
                    AppLog.Error("Auth", "Anonymous cannot be used as a link target.");
                    return AccountLinkResult.Failed;
            }

            SaveLastMethod(platform);
            ClearRecoverCredentials();
            AppLog.Info("Auth", $"Account linked: {platform}. PlayerId={GetPlayerId()}");
            return AccountLinkResult.Linked;
        }
        catch (OperationCanceledException)
        {
            ClearRecoverCredentials();
            AppLog.Warn("Auth", "Account link cancelled.");
            return AccountLinkResult.Cancelled;
        }
        catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
        {
            // Unity AuthenticationExceptionHandler already logged the 409 stack — that is expected.
            AppLog.Warn("Auth", $"External ID already linked to another player ({platform}) — " +
                "recover: leave current session then SignIn existing (not ForceLink).");
            return await SignIntoExistingAfterAlreadyLinkedAsync(platform, cancellationToken);
        }
        catch (Exception e)
        {
            ClearRecoverCredentials();
            AppLog.Error("Auth", $"Account link failed ({platform}): {e.Message}");
            return AccountLinkResult.Failed;
        }
    }

    void ClearRecoverCredentials()
    {
#if UNITY_ANDROID
        _recoverGooglePlayAuthCode = null;
#endif
        _recoverAppleIdentityToken = null;
        _recoverGoogleIdToken = null;
        _recoverFacebookAccessToken = null;
        _recoverOpenIdConnectIdToken = null;
#if UNITY_IOS
        _recoverGameCenterCredentials = null;
#endif
    }

    /// <inheritdoc/>
    public async Task<bool> UnlinkWithAccountAsync(AuthPlatform platform,
        CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn)
        {
            AppLog.Error("Auth", "Cannot unlink — not signed in.");
            return false;
        }

        if (!AuthPlatformKind.IsLinkable(platform))
        {
            AppLog.Error("Auth", "Cannot unlink Anonymous.");
            return false;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await UnlinkIdentityAsync(platform, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (LoadLastMethod() == platform)
            {
                PlayerPrefs.DeleteKey(LastAuthMethodKey);
                PlayerPrefs.Save();
            }

            AppLog.Info("Auth", $"Unlinked {platform}. PlayerId={GetPlayerId()}");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            AppLog.Error("Auth", $"Unlink failed ({platform}): {e.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool IsIdentityLinked(AuthPlatform platform)
    {
        if (!IsSignedIn || !AuthPlatformKind.IsLinkable(platform))
            return false;

        string typeId = AuthPlatformKind.GetExternalIdTypeId(
            platform,
            _providerConfig.OpenIdConnectIdProviderName);
        if (string.IsNullOrEmpty(typeId))
            return false;

        PlayerInfo playerInfo = AuthenticationService.Instance.PlayerInfo;
        return HasIdentity(playerInfo, typeId);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetLinkedIdentityTypeIds()
    {
        if (!IsSignedIn)
            return Array.Empty<string>();

        PlayerInfo playerInfo = AuthenticationService.Instance.PlayerInfo;
        if (playerInfo?.Identities == null || playerInfo.Identities.Count == 0)
            return Array.Empty<string>();

        var ids = new List<string>(playerInfo.Identities.Count);
        foreach (Identity identity in playerInfo.Identities)
        {
            if (identity != null && !string.IsNullOrEmpty(identity.TypeId))
                ids.Add(identity.TypeId);
        }

        return ids;
    }

    static bool HasIdentity(PlayerInfo playerInfo, string typeId)
    {
        if (playerInfo?.Identities == null || string.IsNullOrEmpty(typeId))
            return false;

        foreach (Identity identity in playerInfo.Identities)
        {
            if (identity != null &&
                string.Equals(identity.TypeId, typeId, StringComparison.Ordinal))
                return true;
        }

        return false;
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
            AppLog.Info("Auth", $"Recover begin ({platform}). IsSignedIn={IsSignedIn} PlayerId={GetPlayerId()}");

            if (IsSignedIn)
            {
                await LeaveCurrentSessionForRecoverAsync(cancellationToken);
                PlayerPrefs.DeleteKey(LastAuthMethodKey);
                PlayerPrefs.Save();
                AppLog.Info("Auth", $"Recover session left. IsSignedIn={IsSignedIn}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            AppLog.Info("Auth", $"Recover SignInWith {platform}…");
            await SignInWithMethodAsync(platform, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSignedIn)
            {
                ClearRecoverCredentials();
                AppLog.Error("Auth", $"Recover SignIn failed — still not signed in ({platform}).");
                await EnsureAnonymousFallbackAsync(cancellationToken);
                return AccountLinkResult.Failed;
            }

            SaveLastMethod(platform);
            ClearRecoverCredentials();
            AppLog.Info("Auth", $"Signed into existing account via {platform}. PlayerId={GetPlayerId()}");
            return AccountLinkResult.SignedIntoExisting;
        }
        catch (OperationCanceledException)
        {
            ClearRecoverCredentials();
            AppLog.Warn("Auth", "Recover SignIn cancelled.");
            await EnsureAnonymousFallbackAsync(CancellationToken.None);
            return AccountLinkResult.Cancelled;
        }
        catch (Exception e)
        {
            ClearRecoverCredentials();
            AppLog.Error("Auth", $"Recover SignIn failed ({platform}): {e.Message}");
            await EnsureAnonymousFallbackAsync(cancellationToken);
            return AccountLinkResult.Failed;
        }
    }

    /// <summary>
    /// After recover SignOut, platform SignIn may fail (offline / expired GC signature).
    /// Always try to restore an anonymous session so the game is not stuck NotReady forever.
    /// </summary>
    async Task EnsureAnonymousFallbackAsync(CancellationToken cancellationToken)
    {
        if (IsSignedIn)
            return;

        try
        {
            AppLog.Warn("Auth", "Restoring anonymous session after failed recover…");
            bool ok = await SignInAsync(AuthPlatform.Anonymous, cancellationToken);
            AppLog.Info("Auth", ok && IsSignedIn
                    ? $"[Auth] Anonymous fallback OK. PlayerId={GetPlayerId()}"
                    : "[Auth] Anonymous fallback failed — still signed out.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Auth", $"Anonymous fallback failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes only when Cloud Save looks empty; otherwise SignOut so non-empty anonymous progress is not wiped.
    /// Fail-safe on network/check errors / timeout: SignOut (never Delete).
    /// </summary>
    private async Task LeaveCurrentSessionForRecoverAsync(CancellationToken cancellationToken)
    {
        string orphanId = GetPlayerId();
        bool deleteOrphan = await TryIsCurrentPlayerEffectivelyEmptyAsync(cancellationToken);

        if (deleteOrphan)
        {
            try
            {
                AppLog.Info("Auth", $"Recover: deleting empty anonymous PlayerId={orphanId}…");
                await AuthenticationService.Instance.DeleteAccountAsync();
                AppLog.Info("Auth", $"Deleted empty anonymous PlayerId={orphanId} before recover SignIn.");
                return;
            }
            catch (Exception deleteEx)
            {
                AppLog.Warn("Auth", $"Could not delete empty anonymous ({deleteEx.Message}) — SignOut fallback.");
            }
        }
        else
        {
            AppLog.Info("Auth", $"Recover: SignOut non-empty/unverifiable anonymous PlayerId={orphanId} " +
                "(server data preserved; cannot delete after switch).");
        }

        if (IsSignedIn)
        {
            AuthenticationService.Instance.SignOut(clearCredentials: true);
            AppLog.Info("Auth", "Recover: SignOut(clearCredentials: true) done.");
        }
    }

    /// <summary>
    /// Empty-check with a hard timeout so recover cannot stall before SignOut
    /// (Cloud Save / Economy LoadAll can hang on flaky networks).
    /// </summary>
    private static async Task<bool> TryIsCurrentPlayerEffectivelyEmptyAsync(
        CancellationToken cancellationToken)
    {
        const int EmptyCheckTimeoutMs = RecoverEmptyCheckTimeoutMs;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(EmptyCheckTimeoutMs);

        try
        {
            return await IsCurrentPlayerEffectivelyEmptyAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Warn("Auth", $"Empty-check timed out after {EmptyCheckTimeoutMs}ms — SignOut instead of Delete.");
            return false;
        }
    }

    /// <summary>
    /// True when online Cloud Save has no player keys (or only ignorable internals)
    /// <b>and</b> Economy balances/inventory are empty <b>and</b> local Economy PlayerPrefs
    /// have no cached balances / pending txs. Offline / load failure → false (do not Delete).
    /// </summary>
    private static async Task<bool> IsCurrentPlayerEffectivelyEmptyAsync(CancellationToken cancellationToken)
    {
        if (!NetworkStatus.IsOnline)
        {
            AppLog.Warn("Auth", "Cannot verify empty orphan offline — SignOut instead of Delete.");
            return false;
        }

        // Local deferred / cached economy must block Delete even when server still shows 0.
        if (HasLocalEconomyProgressInPrefs())
        {
            AppLog.Info("Auth", "Empty-check: local economy cache/pending has progress — SignOut instead of Delete.");
            return false;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = await NetworkRequest.WithTimeout(
                CloudSaveService.Instance.Data.Player.LoadAllAsync(),
                cancellationToken);
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
            var balances = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerBalances.GetBalancesAsync(),
                cancellationToken);
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
            var inventory = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerInventory.GetInventoryAsync(),
                cancellationToken);
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
            AppLog.Warn("Auth", $"Empty-check failed ({ex.Message}) — SignOut instead of Delete.");
            return false;
        }
    }

    /// <summary>
    /// Best-effort read of SDK Economy PlayerPrefs without going through typed caches
    /// (recover path may run before game rebinds services).
    /// </summary>
    private static bool HasLocalEconomyProgressInPrefs()
    {
        try
        {
            string cacheJson = PlayerPrefs.GetString("economy_cached_balances", string.Empty);
            if (!string.IsNullOrEmpty(cacheJson) && cacheJson.IndexOf("\"balance\":", StringComparison.Ordinal) >= 0)
            {
                // Any positive balance field — avoid full JSON schema dependency.
                int idx = 0;
                while (idx >= 0 && idx < cacheJson.Length)
                {
                    idx = cacheJson.IndexOf("\"balance\":", idx, StringComparison.Ordinal);
                    if (idx < 0)
                        break;

                    idx += "\"balance\":".Length;
                    while (idx < cacheJson.Length && (cacheJson[idx] == ' ' || cacheJson[idx] == '\t'))
                        idx++;

                    int start = idx;
                    while (idx < cacheJson.Length
                           && (char.IsDigit(cacheJson[idx]) || cacheJson[idx] == '-'))
                        idx++;

                    if (start < idx
                        && long.TryParse(cacheJson.Substring(start, idx - start), out long balance)
                        && balance > 0)
                        return true;
                }
            }

            string pendingJson = PlayerPrefs.GetString("economy_pending_tx", string.Empty);
            if (string.IsNullOrEmpty(pendingJson))
                pendingJson = PlayerPrefs.GetString("economy_pending_adds", string.Empty);

            if (!string.IsNullOrEmpty(pendingJson)
                && pendingJson.IndexOf("\"amount\":", StringComparison.Ordinal) >= 0)
            {
                int idx = 0;
                while (idx >= 0 && idx < pendingJson.Length)
                {
                    idx = pendingJson.IndexOf("\"amount\":", idx, StringComparison.Ordinal);
                    if (idx < 0)
                        break;

                    idx += "\"amount\":".Length;
                    while (idx < pendingJson.Length && (pendingJson[idx] == ' ' || pendingJson[idx] == '\t'))
                        idx++;

                    int start = idx;
                    while (idx < pendingJson.Length
                           && (char.IsDigit(pendingJson[idx]) || pendingJson[idx] == '-'))
                        idx++;

                    if (start < idx
                        && long.TryParse(pendingJson.Substring(start, idx - start), out long amount)
                        && amount != 0)
                        return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Auth", $"Local economy prefs probe failed ({ex.Message}) — treat as non-empty.");
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Reset()
    {
        if (IsSignedIn)
            AuthenticationService.Instance.SignOut(clearCredentials: true);

        PlayerPrefs.DeleteKey(LastAuthMethodKey);
        PlayerPrefs.Save();
        AppLog.Info("Auth", "Session cleared. Next sign-in will create a new anonymous session.");
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSignedIn)
        {
            PlayerPrefs.DeleteKey(LastAuthMethodKey);
            PlayerPrefs.Save();
            AppLog.Warn("Auth", "DeleteAccountAsync: not signed in — cleared local auth prefs only.");
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
            AppLog.Info("Auth", $"Account deleted. Former PlayerId={playerId}");
            return true;
        }
        catch (OperationCanceledException)
        {
            AppLog.Warn("Auth", "DeleteAccountAsync cancelled.");
            return false;
        }
        catch (Exception e)
        {
            AppLog.Error("Auth", $"DeleteAccountAsync failed: {e.Message}");
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
            AuthPlatform.GooglePlayGames => SignInWithGooglePlayGamesAsync(cancellationToken),
            AuthPlatform.Apple => SignInWithAppleAsync(cancellationToken),
            AuthPlatform.AppleGameCenter => SignInWithAppleGameCenterAsync(cancellationToken),
            AuthPlatform.Google => SignInWithGoogleOpenIdAsync(cancellationToken),
            AuthPlatform.Facebook => SignInWithFacebookAsync(cancellationToken),
            AuthPlatform.OpenIdConnect => SignInWithOpenIdConnectAsync(cancellationToken),
            _ => NetworkRequest.WithTimeout(
                AuthenticationService.Instance.SignInAnonymouslyAsync(),
                cancellationToken,
                NetworkRequest.AuthTimeoutMs)
        };

#if UNITY_ANDROID && RAMND_HAS_GOOGLE_PLAY_GAMES
    private async Task SignInWithGooglePlayGamesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_providerConfig.GooglePlayGamesOAuthWebClientId))
        {
            AppLog.Warn("Auth", "TODO(GPGS→UGS): GooglePlayGamesOAuthWebClientId not set; pass WithAuthProviderCredentials if auth fails.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        string serverAuthCode = _recoverGooglePlayAuthCode;
        _recoverGooglePlayAuthCode = null;
        if (string.IsNullOrWhiteSpace(serverAuthCode))
            serverAuthCode = await GetGoogleServerAuthCodeAsync(cancellationToken);
        else
            AppLog.Info("Auth", "Recover: reusing Google Play Games auth code from Link attempt.");

        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.SignInWithGooglePlayGamesAsync(serverAuthCode),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    private async Task LinkWithGooglePlayGamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string serverAuthCode = await GetGoogleServerAuthCodeAsync(cancellationToken);
        _recoverGooglePlayAuthCode = serverAuthCode;
        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.LinkWithGooglePlayGamesAsync(serverAuthCode),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
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
#if !UGS_ENV_PRODUCTION
        PlayGamesPlatform.DebugLogEnabled = true;
#endif
        PlayGamesPlatform.Activate();
        AppLog.Info("Auth",
            $"GPGS auth start appId={GameInfo.ApplicationId} webClientSet={GameInfo.WebClientIdInitialized()} authenticated={PlayGamesPlatform.Instance.IsAuthenticated()}");

        void OnAuthComplete(SignInStatus status)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            if (status == SignInStatus.Canceled)
            {
                AppLog.Warn("Auth",
                    "Google Play Games sign-in Canceled. If the sheet closed by itself, check: " +
                    "Android OAuth SHA-1 (upload key for sideload APK, Play App Signing key for Play installs); " +
                    "Play Console testers; Play Games 'Use next generation IDs' = Off. " +
                    "Do not switch Application Entry to Activity just to debug this — that can drop the launcher icon.");
                tcs.TrySetCanceled();
                return;
            }

            if (status != SignInStatus.Success)
            {
                AppLog.Error("Auth", $"Google Play Games sign-in failed: {status}");
                tcs.TrySetException(new Exception($"Google Play Games sign-in failed: {status}"));
                return;
            }

            AppLog.Info("Auth", "Google Play Games authenticated — requesting server auth code.");
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
            AppLog.Info("Auth", "Google Play Games already authenticated.");
            OnAuthComplete(SignInStatus.Success);
        }
        else
        {
            AppLog.Info("Auth", "Google Play Games: showing manual sign-in UI.");
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
        throw new PlatformNotSupportedException(GooglePlayGamesUnavailableMessage());
    }

    private Task LinkWithGooglePlayGamesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new PlatformNotSupportedException(GooglePlayGamesUnavailableMessage());
    }

    static string GooglePlayGamesUnavailableMessage() =>
#if UNITY_ANDROID
        "Google Play Games plugin is missing. Import Google.Play.Games / com.google.play.games.";
#else
        "Google Play Games is only available on Android.";
#endif
#endif

#if UNITY_IOS
    private async Task SignInWithAppleGameCenterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleGameCenterCredentials credentials = _recoverGameCenterCredentials;
        _recoverGameCenterCredentials = null;
        if (credentials == null || !credentials.IsValid)
            credentials = await RequestAppleGameCenterCredentialsAsync(cancellationToken);
        else
            AppLog.Info("Auth", "Recover: reusing Game Center credentials from Link attempt.");

        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.SignInWithAppleGameCenterAsync(
                credentials.Signature,
                credentials.TeamPlayerId,
                credentials.PublicKeyUrl,
                credentials.Salt,
                credentials.Timestamp),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    private async Task LinkWithAppleGameCenterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppleGameCenterCredentials credentials = await RequestAppleGameCenterCredentialsAsync(cancellationToken);
        _recoverGameCenterCredentials = credentials;
        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.LinkWithAppleGameCenterAsync(
                credentials.Signature,
                credentials.TeamPlayerId,
                credentials.PublicKeyUrl,
                credentials.Salt,
                credentials.Timestamp),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
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

    // —— Portable cloud identities (token bridges from the game) ——

    private async Task SignInWithAppleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_providerConfig.AppleServicesId))
        {
            AppLog.Warn("Auth", "AppleServicesId is empty — ensure UGS Dashboard Apple provider + game config are set.");
        }

        string identityToken = _recoverAppleIdentityToken;
        _recoverAppleIdentityToken = null;
        if (string.IsNullOrWhiteSpace(identityToken))
            identityToken = await RequestAppleIdentityTokenAsync(cancellationToken);
        else
            AppLog.Info("Auth", "Recover: reusing Apple identity token from Link attempt.");

        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.SignInWithAppleAsync(identityToken),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    private async Task LinkWithAppleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_providerConfig.AppleServicesId))
        {
            AppLog.Warn("Auth", "AppleServicesId is empty — ensure UGS Dashboard Apple provider + game config are set.");
        }

        string identityToken = await RequestAppleIdentityTokenAsync(cancellationToken);
        _recoverAppleIdentityToken = identityToken;
        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.LinkWithAppleAsync(identityToken),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
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

    private async Task SignInWithGoogleOpenIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string idToken = _recoverGoogleIdToken;
        _recoverGoogleIdToken = null;
        if (string.IsNullOrWhiteSpace(idToken))
            idToken = await RequestGoogleIdTokenAsync(cancellationToken);
        else
            AppLog.Info("Auth", "Recover: reusing Google OpenID id_token from Link attempt.");

        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.SignInWithGoogleAsync(idToken),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    private async Task LinkWithGoogleOpenIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string idToken = await RequestGoogleIdTokenAsync(cancellationToken);
        _recoverGoogleIdToken = idToken;
        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.LinkWithGoogleAsync(idToken),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    private async Task<string> RequestGoogleIdTokenAsync(CancellationToken cancellationToken)
    {
        if (_providerConfig.RequestGoogleIdTokenAsync == null)
        {
            throw new InvalidOperationException(
                "Google OpenID: RequestGoogleIdTokenAsync is not set. " +
                "Wire Google Sign-In via GameServicesAuthProviderConfig.");
        }

        string idToken = await _providerConfig.RequestGoogleIdTokenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(idToken))
            throw new InvalidOperationException("Google OpenID: id_token is empty.");

        return idToken;
    }

    private async Task SignInWithFacebookAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string accessToken = _recoverFacebookAccessToken;
        _recoverFacebookAccessToken = null;
        if (string.IsNullOrWhiteSpace(accessToken))
            accessToken = await RequestFacebookAccessTokenAsync(cancellationToken);
        else
            AppLog.Info("Auth", "Recover: reusing Facebook access token from Link attempt.");

        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.SignInWithFacebookAsync(accessToken),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    private async Task LinkWithFacebookAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string accessToken = await RequestFacebookAccessTokenAsync(cancellationToken);
        _recoverFacebookAccessToken = accessToken;
        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.LinkWithFacebookAsync(accessToken),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    private async Task<string> RequestFacebookAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_providerConfig.RequestFacebookAccessTokenAsync == null)
        {
            throw new InvalidOperationException(
                "Facebook: RequestFacebookAccessTokenAsync is not set. " +
                "Wire Facebook Login via GameServicesAuthProviderConfig.");
        }

        string accessToken = await _providerConfig.RequestFacebookAccessTokenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Facebook: access token is empty.");

        return accessToken;
    }

    private async Task SignInWithOpenIdConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string idProviderName = RequireOpenIdConnectIdProviderName();
        string idToken = _recoverOpenIdConnectIdToken;
        _recoverOpenIdConnectIdToken = null;
        if (string.IsNullOrWhiteSpace(idToken))
            idToken = await RequestOpenIdConnectIdTokenAsync(cancellationToken);
        else
            AppLog.Info("Auth", "Recover: reusing OpenID Connect id_token from Link attempt.");

        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.SignInWithOpenIdConnectAsync(idProviderName, idToken),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    private async Task LinkWithOpenIdConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string idProviderName = RequireOpenIdConnectIdProviderName();
        string idToken = await RequestOpenIdConnectIdTokenAsync(cancellationToken);
        _recoverOpenIdConnectIdToken = idToken;
        cancellationToken.ThrowIfCancellationRequested();
        await NetworkRequest.WithTimeout(
            AuthenticationService.Instance.LinkWithOpenIdConnectAsync(idProviderName, idToken),
            cancellationToken,
            NetworkRequest.AuthTimeoutMs);
    }

    string RequireOpenIdConnectIdProviderName()
    {
        if (string.IsNullOrWhiteSpace(_providerConfig.OpenIdConnectIdProviderName))
        {
            throw new InvalidOperationException(
                "OpenID Connect: OpenIdConnectIdProviderName is not set. " +
                "Use the Id Provider name from UGS Dashboard.");
        }

        return _providerConfig.OpenIdConnectIdProviderName.Trim();
    }

    private async Task<string> RequestOpenIdConnectIdTokenAsync(CancellationToken cancellationToken)
    {
        if (_providerConfig.RequestOpenIdConnectIdTokenAsync == null)
        {
            throw new InvalidOperationException(
                "OpenID Connect: RequestOpenIdConnectIdTokenAsync is not set. " +
                "Wire the IdP token fetch via GameServicesAuthProviderConfig.");
        }

        string idToken = await _providerConfig.RequestOpenIdConnectIdTokenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(idToken))
            throw new InvalidOperationException("OpenID Connect: id_token is empty.");

        return idToken;
    }

    async Task UnlinkIdentityAsync(AuthPlatform platform, CancellationToken cancellationToken)
    {
        Task unlinkTask = platform switch
        {
            AuthPlatform.GooglePlayGames => AuthenticationService.Instance.UnlinkGooglePlayGamesAsync(),
            AuthPlatform.Apple => AuthenticationService.Instance.UnlinkAppleAsync(),
            AuthPlatform.AppleGameCenter => AuthenticationService.Instance.UnlinkAppleGameCenterAsync(),
            AuthPlatform.Google => AuthenticationService.Instance.UnlinkGoogleAsync(),
            AuthPlatform.Facebook => AuthenticationService.Instance.UnlinkFacebookAsync(),
            AuthPlatform.OpenIdConnect => AuthenticationService.Instance.UnlinkOpenIdConnectAsync(
                RequireOpenIdConnectIdProviderName()),
            _ => throw new InvalidOperationException($"Cannot unlink {platform}."),
        };

        await NetworkRequest.WithTimeout(unlinkTask, cancellationToken, NetworkRequest.AuthTimeoutMs);
    }

    static bool IsTransportFailure(Exception exception)
    {
        for (Exception walk = exception; walk != null; walk = walk.InnerException)
        {
            if (walk is TimeoutException)
                return true;
            if (walk is SocketException)
                return true;
            if (walk is HttpRequestException)
                return true;
        }

        return false;
    }
}
