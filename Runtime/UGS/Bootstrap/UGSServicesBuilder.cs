using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Builder for all UGS services. Performs full initialization in one
/// BuildAsync call: Unity Services → Auth → Analytics → project callback → Ads.
/// <para>
/// <b>Extensibility:</b> for another SDK (Firebase, PlayFab, etc.) create a similar
/// builder following the same pattern. Core interfaces stay unchanged.
/// </para>
/// </summary>
public sealed class UGSServicesBuilder
{
    private bool                                      _forceAnonymous;
    private IAdsManager                               _adsManager;
    private Func<IAuthService, Task>                  _onAuthenticated;
    private string[]                                  _profanityWords;
    private Regex                                     _profanityPattern;
    private NameValidatorConfig                       _nameValidator;
    private GameServicesAuthProviderConfig            _authCredentials = GameServicesAuthProviderConfig.Empty;

    /// <summary>
    /// Force anonymous sign-in on all platforms.
    /// Convenient during development while Google/Apple auth is not ready.
    /// </summary>
    public UGSServicesBuilder WithForceAnonymous(bool force = true)
    {
        _forceAnonymous = force;
        return this;
    }

    /// <summary>
    /// Optional platform provider keys/identifiers (Google Play Games, Sign in with Apple → UGS).
    /// If unset, linking methods should report an error without crashing — see <see cref="UGSAuthService"/>.
    /// </summary>
    public UGSServicesBuilder WithAuthProviderCredentials(GameServicesAuthProviderConfig credentials)
    {
        _authCredentials = credentials ?? GameServicesAuthProviderConfig.Empty;
        return this;
    }

    /// <summary>
    /// Sets the full nickname profanity-filter config (e.g. from a game-side ScriptableObject via ToValidatorConfig()).
    /// Takes priority over separate WithProfanityFilter calls when non-null.
    /// </summary>
    public UGSServicesBuilder WithNameValidator(NameValidatorConfig config)
    {
        _nameValidator = config;
        return this;
    }

    /// <summary>
    /// Sets banned words/substrings for nickname validation.
    /// Matching is case-insensitive.
    /// <para>Combined with <see cref="WithProfanityFilter(Regex)"/> into one config if WithNameValidator is not set.</para>
    /// </summary>
    public UGSServicesBuilder WithProfanityFilter(params string[] bannedWords)
    {
        _profanityWords = bannedWords;
        return this;
    }

    /// <summary>
    /// Sets a regex for nickname validation.
    /// Runs after the banned-word check.
    /// </summary>
    public UGSServicesBuilder WithProfanityFilter(Regex bannedPattern)
    {
        _profanityPattern = bannedPattern;
        return this;
    }

    /// <summary>
    /// Sets the ads manager implementation.
    /// Default is <see cref="TestAdsManager"/> (stub without a real SDK).
    /// </summary>
    public UGSServicesBuilder WithAds(IAdsManager adsManager)
    {
        _adsManager = adsManager;
        return this;
    }

    /// <summary>
    /// Registers a callback invoked immediately after successful auth.
    /// <para>
    /// Initialize Economy and Items services here — they require an active UGS session.
    /// The callback is not invoked if auth fails.
    /// </para>
    /// </summary>
    public UGSServicesBuilder OnAuthenticated(Func<IAuthService, Task> callback)
    {
        _onAuthenticated = callback;
        return this;
    }

    /// <summary>
    /// Runs full initialization in this order:
    /// <list type="number">
    /// <item>UnityServices.InitializeAsync()</item>
    /// <item>Auth via <see cref="UGSAuthService"/></item>
    /// <item>UGS Analytics (only on successful auth)</item>
    /// <item>OnAuthenticated callback (Economy, Items, etc.)</item>
    /// <item>Ads (independent of auth)</item>
    /// </list>
    /// </summary>
    public async Task<IGameServices> BuildAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await UnityServices.InitializeAsync();

        cancellationToken.ThrowIfCancellationRequested();

        var authNaming = ResolveNameValidator();
        var auth       = new UGSAuthService(authNaming, _authCredentials);
        var platform   = ResolvePlatform();
        bool signedIn = await auth.SignInAsync(platform, cancellationToken);

        Debug.Log($"[SDK] Auth: SignedIn={signedIn}, PlayerId={auth.GetPlayerId()}");

        IAnalyticsSystem analytics = null;
        if (signedIn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AuthenticationSdkReadiness.WaitForPlayerSessionStableAsync(cancellationToken);

            // TODO(analytics-consent): UGS Analytics v6 — migrate from deprecated StartDataCollection to EndUserConsent / store policies.
            analytics = new UGSAnalyticSystem(
                auth.GetPlayerId(),
                Unity.Services.Analytics.AnalyticsService.Instance);
        }

        ILeaderboardService leaderboards = null;
        if (signedIn)
        {
            leaderboards = new UGSLeaderboardService();
            Debug.Log("[SDK] Leaderboards initialized.");
        }
        else
        {
            Debug.LogWarning("[SDK] Leaderboards skipped — user not authenticated. GameServicesLocator.Services.Leaderboards will be null.");
        }

        if (signedIn && _onAuthenticated != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _onAuthenticated(auth);
        }

        var ads = _adsManager ?? new TestAdsManager();
        ads.Initialize();

        cancellationToken.ThrowIfCancellationRequested();

        var services = new UGSGameServices(auth, analytics, ads, leaderboards);
        GameServicesLocator.Set(services);
        return services;
    }

    private NameValidatorConfig ResolveNameValidator() =>
        _nameValidator ?? new NameValidatorConfig(_profanityWords, _profanityPattern);

    private AuthPlatform ResolvePlatform()
    {
        if (_forceAnonymous) return AuthPlatform.Anonymous;

#if UNITY_EDITOR
        return AuthPlatform.Anonymous;
#elif UNITY_ANDROID
        return AuthPlatform.GooglePlayGames;
#elif UNITY_IOS
        return AuthPlatform.Apple;
#else
        return AuthPlatform.Anonymous;
#endif
    }
}
