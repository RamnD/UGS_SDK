/// <summary>
/// Identifiers for <see cref="GameServicesSync.RefreshAsync"/>.
/// Façade services (RemoteConfig … Ads) are registered by the UGS builder;
/// project-typed layers (Economy / Items / CloudSave) must be registered by the game.
/// </summary>
public enum GameServiceId
{
    /// <summary>UGS Remote Config re-fetch (registered by the builder when enabled).</summary>
    RemoteConfig,

    /// <summary>Achievement Cloud Save / local cache reload.</summary>
    Achievements,

    /// <summary>Platform achievement pending-report flush (Google Play Games / Game Center).</summary>
    PlatformAchievements,

    /// <summary>Analytics offline-queue drain.</summary>
    Analytics,

    /// <summary>Leaderboard soft refresh (no-op or re-query depending on registration).</summary>
    Leaderboards,

    /// <summary>Ads session / mediation readiness refresh.</summary>
    Ads,

    /// <summary>Currency balances + pending economy queue flush (game-registered).</summary>
    Economy,

    /// <summary>Durable / consumable inventory refresh (game-registered).</summary>
    Items,

    /// <summary>Cloud Save pull / conflict check (game-registered).</summary>
    CloudSave,

    /// <summary>
    /// Economy purchase catalog refresh (<see cref="IEconomyPurchaseCatalog.RefreshAsync"/>).
    /// Game-registered when the project uses a dynamic shop catalog.
    /// </summary>
    PurchaseCatalog,
}
