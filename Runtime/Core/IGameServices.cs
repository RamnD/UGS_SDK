/// <summary>
/// Единая точка доступа к SDK-сервисам: авторизация, аналитика, реклама, лидерборды.
/// Создаётся билдером (например UGSServicesBuilder) на сторте приложения; экземпляр регистрируется в <see cref="GameServicesLocator"/>.
/// <para>
/// Сервисы экономики (<see cref="IInventoryService{TCurrency}"/>), предметов
/// (<see cref="IItemService{TItem}"/>) и облачных сохранений (<see cref="ICloudSaveService{TKey}"/>)
/// не входят в фасад — они generic и привязаны к проектным типам.
/// Доступ к ним через MonoBehaviour-мосты (напр. PlayerData, PlayerItemsData, PlayerSaveData).
/// </para>
/// </summary>
public interface IGameServices
{
    /// <summary>
    /// Сервис авторизации. Всегда не null после успешной инициализации билдером.
    /// </summary>
    IAuthService Auth { get; }

    /// <summary>
    /// Сервис аналитики. Null если авторизация не прошла (<c>Auth.IsSignedIn == false</c>).
    /// После регистрации фасада в <see cref="GameServicesLocator"/> используйте <c>Services.Analytics</c>.
    /// </summary>
    IAnalyticsSystem Analytics { get; }

    /// <summary>
    /// Сервис рекламы. Всегда не null после инициализации билдером.
    /// После регистрации используйте <c>Services.Ads</c>.
    /// </summary>
    IAdsManager Ads { get; }

    /// <summary>
    /// Сервис таблицы лидеров. Null если авторизация не прошла.
    /// После регистрации используйте <c>Services.Leaderboards</c>.
    /// </summary>
    ILeaderboardService Leaderboards { get; }

    /// <summary>True если авторизация прошла успешно.</summary>
    bool IsAuthenticated { get; }
}
