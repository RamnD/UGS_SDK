using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Строитель всех UGS-сервисов. Выполняет полную инициализацию за один вызов
/// BuildAsync: Unity Services → Auth → Analytics → колбэк проекта → Ads.
/// <para>
/// <b>Расширяемость:</b> для другого SDK (Firebase, PlayFab и т.д.) создайте аналогичный
/// builder, реализующий тот же паттерн. Интерфейсы из Core останутся без изменений.
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
    /// Принудительный анонимный вход на всех платформах.
    /// Удобно на этапе разработки, пока Google/Apple авторизация не готова.
    /// </summary>
    public UGSServicesBuilder WithForceAnonymous(bool force = true)
    {
        _forceAnonymous = force;
        return this;
    }

    /// <summary>
    /// Необязательные ключи/идентификаторы платформенных провайдеров (Google Play Games, Sign in with Apple → UGS).
    /// Если не заданы, соответствующие методы привязки должны сообщать об ошибке без падения приложения — см. <see cref="UGSAuthService"/>.
    /// </summary>
    public UGSServicesBuilder WithAuthProviderCredentials(GameServicesAuthProviderConfig credentials)
    {
        _authCredentials = credentials ?? GameServicesAuthProviderConfig.Empty;
        return this;
    }

    /// <summary>
    /// Целиком задаёт конфиг антицензора ника (например из ScriptableObject на стороне игры через ToValidatorConfig()).
    /// Имеет приоритет над отдельными вызовами WithProfanityFilter, если указан не null.
    /// </summary>
    public UGSServicesBuilder WithNameValidator(NameValidatorConfig config)
    {
        _nameValidator = config;
        return this;
    }

    /// <summary>
    /// Устанавливает список запрещённых слов/подстрок для проверки никнейма.
    /// Проверка выполняется без учёта регистра.
    /// <para>Совмещается с <see cref="WithProfanityFilter(Regex)"/> в один конфиг если WithNameValidator не задан.</para>
    /// </summary>
    public UGSServicesBuilder WithProfanityFilter(params string[] bannedWords)
    {
        _profanityWords = bannedWords;
        return this;
    }

    /// <summary>
    /// Устанавливает регулярное выражение для проверки никнейма.
    /// Выполняется после проверки запрещённых слов.
    /// </summary>
    public UGSServicesBuilder WithProfanityFilter(Regex bannedPattern)
    {
        _profanityPattern = bannedPattern;
        return this;
    }

    /// <summary>
    /// Устанавливает реализацию рекламного менеджера.
    /// По умолчанию используется <see cref="TestAdsManager"/> (stub без реального SDK).
    /// </summary>
    public UGSServicesBuilder WithAds(IAdsManager adsManager)
    {
        _adsManager = adsManager;
        return this;
    }

    /// <summary>
    /// Регистрирует колбэк, вызываемый сразу после успешной авторизации.
    /// <para>
    /// Здесь следует инициализировать Economy и Items-сервисы, так как они требуют
    /// активной сессии UGS. Колбэк не вызывается, если авторизация провалилась.
    /// </para>
    /// </summary>
    public UGSServicesBuilder OnAuthenticated(Func<IAuthService, Task> callback)
    {
        _onAuthenticated = callback;
        return this;
    }

    /// <summary>
    /// Выполняет полную инициализацию в следующем порядке:
    /// <list type="number">
    /// <item>UnityServices.InitializeAsync()</item>
    /// <item>Авторизация через <see cref="UGSAuthService"/></item>
    /// <item>UGS Analytics (только при успешной авторизации)</item>
    /// <item>Колбэк OnAuthenticated (Economy, Items и прочее)</item>
    /// <item>Реклама (не зависит от авторизации)</item>
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

            // TODO(analytics-consent): UGS Analytics v6 — перейти с устаревшего StartDataCollection на EndUserConsent / политики магазинов.
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
