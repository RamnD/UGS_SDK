/// <summary>
/// Mock <see cref="IGameServices"/> implementation.
/// </summary>
public sealed class MockGameServices : IGameServices
{
    /// <inheritdoc/>
    public IAuthService      Auth         { get; }

    /// <inheritdoc/>
    public IAnalyticsSystem  Analytics    { get; }

    /// <inheritdoc/>
    public IAdsManager       Ads          { get; }

    /// <inheritdoc/>
    public ILeaderboardService Leaderboards { get; }

    /// <inheritdoc/>
    public IRemoteConfigService RemoteConfig { get; }

    /// <inheritdoc/>
    public IAchievementService Achievements { get; }

    /// <inheritdoc/>
    public bool IsAuthenticated => Auth.IsSignedIn;

    public MockGameServices(
        IAuthService          auth         = null,
        IAnalyticsSystem      analytics    = null,
        IAdsManager           ads          = null,
        ILeaderboardService   leaderboards = null,
        IRemoteConfigService  remoteConfig = null,
        IAchievementService   achievements = null)
    {
        Auth         = auth         ?? new MockAuthService();
        Analytics    = analytics    ?? new MockAnalyticsSystem();
        Ads          = ads          ?? new MockAdsManager();
        Leaderboards = leaderboards ?? new MockLeaderboardService();
        RemoteConfig = remoteConfig ?? new MockRemoteConfigService();
        Achievements = achievements ?? new MockAchievementService();
    }

    /// <summary>
    /// Instance with all default mock services. Registered in <see cref="GameServicesLocator"/>.
    /// </summary>
    public static MockGameServices CreateDefault()
    {
        var instance = new MockGameServices();
        _ = instance.Auth.SignInAsync(AuthPlatform.Anonymous);
        instance.Ads.Initialize();
        GameServicesLocator.Set(instance);
        return instance;
    }
}
