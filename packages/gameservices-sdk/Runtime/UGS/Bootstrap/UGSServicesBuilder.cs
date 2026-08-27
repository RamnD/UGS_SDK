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
    private bool                                      _useCachedAnalytics;
    private bool                                      _useRemoteConfig;
    private bool                                      _useAchievements;
    private PlatformAchievementsOptions               _platformAchievements;

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
    /// Wraps analytics with a disk-backed offline queue (opt-in).
    /// </summary>
    public UGSServicesBuilder WithCachedAnalytics(bool enabled = true)
    {
        _useCachedAnalytics = enabled;
        return this;
    }

    /// <summary>
    /// Enables UGS Remote Config fetch after auth. Values are cached in PlayerPrefs for offline reads.
    /// </summary>
    public UGSServicesBuilder WithRemoteConfig(bool enabled = true)
    {
        _useRemoteConfig = enabled;
        return this;
    }

    /// <summary>
    /// Enables the portable achievements module backed by UGS Cloud Save.
    /// </summary>
    public UGSServicesBuilder WithAchievements(bool enabled = true)
    {
        _useAchievements = enabled;
        return this;
    }

    /// <summary>
    /// Enables native platform achievement reporting (Google Play Games / Apple Game Center).
    /// The game supplies the portable-to-platform id mapping; the SDK chooses the
    /// active bridge for the current runtime platform.
    /// </summary>
    public UGSServicesBuilder WithPlatformAchievements(
        IAchievementPlatformMapper mapper,
        bool enabled = true,
        bool showAppleCompletionBanner = true)
    {
        _platformAchievements = enabled && mapper != null
            ? new PlatformAchievementsOptions
            {
                Mapper = mapper,
                ShowAppleCompletionBanner = showAppleCompletionBanner,
            }
            : null;
        return this;
    }

    /// <summary>
    /// Runs full initialization in this order:
    /// <list type="number">
    /// <item>UnityServices.InitializeAsync()</item>
    /// <item>Auth via <see cref="UGSAuthService"/></item>
    /// <item>UGS Analytics (only on successful auth)</item>
    /// <item>Remote Config fetch (opt-in, after auth)</item>
    /// <item>Achievements warmup (opt-in, after auth)</item>
    /// <item>OnAuthenticated callback (Economy, Items, etc.)</item>
    /// <item>Ads (independent of auth)</item>
    /// </list>
    /// Continuations stay on Unity's synchronization context (no <c>ConfigureAwait(false)</c>).
    /// </summary>
    /// <param name="cancellationToken">Cancels bootstrap (e.g. MonoBehaviour destroy token).</param>
    /// <returns>The built façade (also registered in <see cref="GameServicesLocator"/>).</returns>
    public async Task<IGameServices> BuildAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await UGSUnityServicesInitializer.EnsureInitializedAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var authNaming = ResolveNameValidator();
        var auth       = new UGSAuthService(authNaming, _authCredentials);
        var platform   = ResolvePlatform();

        CachedAnalyticsSystem cachedAnalytics = null;
        IAnalyticsSystem analytics = null;

        if (_useCachedAnalytics)
        {
            cachedAnalytics = CachedAnalyticsSystem.CreatePreAuth();
            analytics = cachedAnalytics;
            GameServicesLocator.Set(new UGSGameServices(
                auth,
                analytics,
                _adsManager ?? new TestAdsManager(),
                leaderboards: null,
                remoteConfig: null,
                achievements: null,
                platformAchievements: null));
        }

        bool signedIn = await auth.SignInAsync(platform, cancellationToken);

        AppLog.Info("SDK", $"Auth: SignedIn={signedIn}, PlayerId={auth.GetPlayerId()}");

        if (signedIn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AuthenticationSdkReadiness.WaitForPlayerSessionStableAsync(cancellationToken);

            // TODO(analytics-consent): UGS Analytics v6 — migrate from deprecated StartDataCollection to EndUserConsent / store policies.
            var ugsAnalytics = new UGSAnalyticSystem(
                auth.GetPlayerId(),
                Unity.Services.Analytics.AnalyticsService.Instance);

            if (cachedAnalytics != null)
                cachedAnalytics.AttachInner(ugsAnalytics, Unity.Services.Analytics.AnalyticsService.Instance);
            else
                analytics = ugsAnalytics;
        }

        ILeaderboardService leaderboards = null;
        IRemoteConfigService remoteConfig = null;
        IAchievementService achievements = null;
        IPlatformAchievementBridge platformAchievements = null;
        if (signedIn)
        {
            leaderboards = new UGSLeaderboardService();
            AppLog.Info("SDK", "Leaderboards initialized.");

            if (_useRemoteConfig)
            {
                remoteConfig = new UGSRemoteConfigService();
                try
                {
                    await remoteConfig.FetchAsync(cancellationToken);
                    AppLog.Info("SDK", "Remote Config initialized.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (RemoteConfigOperationException ex)
                {
                    AppLog.Warn("SDK", $"Remote Config fetch failed: {ex.Message}");
                }
            }

            if (_useAchievements)
            {
                var ugsAchievements = new UGSAchievementService();
                try
                {
                    await ugsAchievements.WarmupAsync(cancellationToken);
                    achievements = ugsAchievements;
                    AppLog.Info("SDK", "Achievements initialized.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (AchievementOperationException ex)
                {
                    AppLog.Warn("SDK", $"Achievements warmup failed: {ex.Message}");
                    achievements = ugsAchievements;
                }
            }

            if (_platformAchievements?.Mapper != null)
            {
                platformAchievements = PlatformAchievementBridgeFactory.Create(_platformAchievements);
                if (platformAchievements is NullPlatformAchievementBridge)
                    AppLog.Info("SDK", "Platform Achievements disabled for the current runtime platform.");
                else
                    AppLog.Info("SDK", "Platform Achievements initialized.");
            }
        }
        else
        {
            AppLog.Warn("SDK", "Leaderboards skipped — user not authenticated. GameServicesLocator.Services.Leaderboards will be null.");
            if (_useRemoteConfig)
                AppLog.Warn("SDK", "Remote Config skipped — user not authenticated.");
            if (_useAchievements)
                AppLog.Warn("SDK", "Achievements skipped — user not authenticated.");
            if (_platformAchievements?.Mapper != null)
                AppLog.Warn("SDK", "Platform Achievements skipped — user not authenticated.");
        }

        if (signedIn && _onAuthenticated != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _onAuthenticated(auth);
        }

        var ads = _adsManager ?? new TestAdsManager();
        ads.Initialize();

        cancellationToken.ThrowIfCancellationRequested();

        var services = new UGSGameServices(
            auth,
            analytics,
            ads,
            leaderboards,
            remoteConfig,
            achievements,
            platformAchievements);
        GameServicesLocator.Set(services);
        RegisterFacadeSyncHandlers(analytics, ads, leaderboards, remoteConfig, achievements, platformAchievements);
        return services;
    }

    static void RegisterFacadeSyncHandlers(
        IAnalyticsSystem analytics,
        IAdsManager ads,
        ILeaderboardService leaderboards,
        IRemoteConfigService remoteConfig,
        IAchievementService achievements,
        IPlatformAchievementBridge platformAchievements)
    {
        if (remoteConfig != null)
        {
            GameServicesSync.Register(GameServiceId.RemoteConfig, async ct =>
            {
                await remoteConfig.FetchAsync(ct);
            });
        }
        else
            GameServicesSync.Unregister(GameServiceId.RemoteConfig);

        if (achievements != null)
        {
            GameServicesSync.Register(GameServiceId.Achievements, async ct =>
            {
                await achievements.FlushAsync(ct);
            });
        }
        else
            GameServicesSync.Unregister(GameServiceId.Achievements);

        if (platformAchievements != null)
        {
            GameServicesSync.Register(GameServiceId.PlatformAchievements, async ct =>
            {
                await platformAchievements.FlushAsync(ct);
            });
        }
        else
            GameServicesSync.Unregister(GameServiceId.PlatformAchievements);

        if (analytics != null)
        {
            GameServicesSync.Register(GameServiceId.Analytics, _ =>
            {
                analytics.Flush();
                return Task.CompletedTask;
            });
        }
        else
            GameServicesSync.Unregister(GameServiceId.Analytics);

        if (ads is ILevelPlayAdsController levelPlay)
        {
            GameServicesSync.Register(GameServiceId.Ads, _ =>
            {
                levelPlay.EnsurePreloadedUnitsReady();
                return Task.CompletedTask;
            });
        }
        else
            GameServicesSync.Unregister(GameServiceId.Ads);

        // Leaderboards have no durable local cache to refresh on reconnect.
        GameServicesSync.Unregister(GameServiceId.Leaderboards);
        _ = leaderboards;
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
        return AuthPlatform.AppleGameCenter;
#else
        return AuthPlatform.Anonymous;
#endif
    }
}
