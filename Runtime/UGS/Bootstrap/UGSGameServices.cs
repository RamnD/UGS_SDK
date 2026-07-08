using System;

/// <summary>
/// Concrete <see cref="IGameServices"/> implementation.
/// Created only inside <see cref="UGSServicesBuilder.BuildAsync"/> — do not instantiate directly.
/// </summary>
public sealed class UGSGameServices : IGameServices
{
    /// <inheritdoc/>
    public IAuthService      Auth          { get; }

    /// <inheritdoc/>
    public IAnalyticsSystem  Analytics     { get; }

    /// <inheritdoc/>
    public IAdsManager       Ads           { get; }

    /// <inheritdoc/>
    public ILeaderboardService Leaderboards { get; }

    /// <inheritdoc/>
    public IRemoteConfigService RemoteConfig { get; }

    /// <inheritdoc/>
    public IAchievementService Achievements { get; }

    /// <inheritdoc/>
    public bool              IsAuthenticated { get; }

    internal UGSGameServices(
        IAuthService          auth,
        IAnalyticsSystem      analytics,
        IAdsManager           ads,
        ILeaderboardService   leaderboards,
        IRemoteConfigService  remoteConfig = null,
        IAchievementService   achievements = null)
    {
        Auth            = auth          ?? throw new ArgumentNullException(nameof(auth));
        Analytics       = analytics;     // null allowed (not authenticated)
        Ads             = ads            ?? throw new ArgumentNullException(nameof(ads));
        Leaderboards    = leaderboards;  // null allowed (not authenticated)
        RemoteConfig    = remoteConfig;  // null allowed (not enabled / not authenticated)
        Achievements    = achievements;  // null allowed (not enabled / not authenticated)
        IsAuthenticated = auth.IsSignedIn;
    }
}
