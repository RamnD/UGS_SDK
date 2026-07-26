/// <summary>
/// Identifiers for <see cref="GameServicesSync.RefreshAsync"/>.
/// Project layers (Economy / Items / CloudSave) are registered by the game;
/// façade services by the UGS builder.
/// </summary>
public enum GameServiceId
{
    RemoteConfig,
    Achievements,
    Analytics,
    Leaderboards,
    Ads,
    Economy,
    Items,
    CloudSave,
}
