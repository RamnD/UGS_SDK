/// <summary>
/// Builder options for the optional platform achievement bridge layer.
/// The game supplies the id mapper; the SDK chooses the active bridge for the
/// current runtime platform.
/// </summary>
public sealed class PlatformAchievementsOptions
{
    /// <summary>Game-owned mapper from portable ids to store-specific ids.</summary>
    public IAchievementPlatformMapper Mapper { get; set; }

    /// <summary>
    /// Enables Google Play Games reporting on Android when the plugin is present.
    /// </summary>
    public bool UseGooglePlayGames { get; set; } = true;

    /// <summary>
    /// Enables Apple Game Center reporting on iOS when Apple.GameKit is present.
    /// </summary>
    public bool UseAppleGameCenter { get; set; } = true;

    /// <summary>
    /// Whether Game Center should show the native completion banner on unlock.
    /// </summary>
    public bool ShowAppleCompletionBanner { get; set; } = true;
}
