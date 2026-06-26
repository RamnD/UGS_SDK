/// <summary>
/// Single access point to SDK services: auth, analytics, ads, leaderboards.
/// Created by a builder (e.g. UGSServicesBuilder) at app start; the instance is registered in <see cref="GameServicesLocator"/>.
/// <para>
/// Economy (<see cref="IInventoryService{TCurrency}"/>), items
/// (<see cref="IItemService{TItem}"/>), and cloud save (<see cref="ICloudSaveService{TKey}"/>)
/// are not part of the façade — they are generic and tied to project types.
/// Access them via MonoBehaviour bridges (e.g. PlayerData, PlayerItemsData, PlayerSaveData).
/// </para>
/// </summary>
public interface IGameServices
{
    /// <summary>
    /// Auth service. Always non-null after successful builder initialization.
    /// </summary>
    IAuthService Auth { get; }

    /// <summary>
    /// Analytics service. Null if auth failed (<c>Auth.IsSignedIn == false</c>).
    /// After registering the façade in <see cref="GameServicesLocator"/>, use <c>Services.Analytics</c>.
    /// </summary>
    IAnalyticsSystem Analytics { get; }

    /// <summary>
    /// Ads service. Always non-null after builder initialization.
    /// After registration, use <c>Services.Ads</c>.
    /// </summary>
    IAdsManager Ads { get; }

    /// <summary>
    /// Leaderboard service. Null if auth failed.
    /// After registration, use <c>Services.Leaderboards</c>.
    /// </summary>
    ILeaderboardService Leaderboards { get; }

    /// <summary>
    /// Remote Config service. Null if not enabled or auth failed.
    /// After registration, use <c>Services.RemoteConfig</c>.
    /// </summary>
    IRemoteConfigService RemoteConfig { get; }

    /// <summary>True if auth succeeded.</summary>
    bool IsAuthenticated { get; }
}
