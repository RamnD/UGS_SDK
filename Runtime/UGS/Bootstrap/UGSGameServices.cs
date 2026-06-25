using System;

/// <summary>
/// Конкретная реализация <see cref="IGameServices"/>.
/// Создаётся только внутри <see cref="UGSServicesBuilder.BuildAsync"/> — не инстанциируйте напрямую.
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
        Analytics       = analytics;     // null допустим (нет авторизации)
        Ads             = ads            ?? throw new ArgumentNullException(nameof(ads));
        Leaderboards    = leaderboards;  // null допустим (нет авторизации)
        IsAuthenticated = auth.IsSignedIn;
    }
}
