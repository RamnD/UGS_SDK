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
    public bool              IsAuthenticated { get; }

    internal UGSGameServices(
        IAuthService       auth,
        IAnalyticsSystem   analytics,
        IAdsManager        ads,
        ILeaderboardService leaderboards)
    {
        Auth            = auth          ?? throw new ArgumentNullException(nameof(auth));
        Analytics       = analytics;     // null allowed (not authenticated)
        Ads             = ads            ?? throw new ArgumentNullException(nameof(ads));
        Leaderboards    = leaderboards;  // null allowed (not authenticated)
        IsAuthenticated = auth.IsSignedIn;
    }
}
