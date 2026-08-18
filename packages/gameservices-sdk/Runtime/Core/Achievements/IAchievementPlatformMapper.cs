/// <summary>
/// Maps a game-defined achievement id to optional platform-native ids.
/// The game owns the catalog and decides which achievements should be mirrored
/// to Google Play Games and/or Apple Game Center.
/// </summary>
public interface IAchievementPlatformMapper
{
    /// <summary>
    /// Resolves the Google Play Games achievement id for a game achievement.
    /// </summary>
    bool TryGetGooglePlayAchievementId(string achievementId, out string platformId);

    /// <summary>
    /// Resolves the Apple Game Center achievement id for a game achievement.
    /// </summary>
    bool TryGetAppleGameCenterAchievementId(string achievementId, out string platformId);
}
