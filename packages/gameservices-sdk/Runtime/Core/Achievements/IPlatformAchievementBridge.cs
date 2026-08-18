using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Best-effort bridge from the game's portable achievement ids to native
/// platform achievement APIs such as Google Play Games or Apple Game Center.
/// This bridge must never be the source of truth for unlocks or rewards —
/// it mirrors the game-owned achievement state outward.
/// </summary>
public interface IPlatformAchievementBridge
{
    /// <summary>
    /// Reports total progress for an achievement.
    /// Implementations normalize the values to the native platform format.
    /// No-op when the achievement is not mapped for the current platform.
    /// </summary>
    Task ReportProgressAsync(
        string achievementId,
        double currentProgress,
        double targetProgress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports an unlocked achievement.
    /// No-op when the achievement is not mapped for the current platform.
    /// </summary>
    Task ReportUnlockAsync(
        string achievementId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes pending platform reports if the native platform is available.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears local pending/report state for account delete or account switch.
    /// Does not remove achievements from the native platform profile.
    /// </summary>
    void ClearLocalCache();
}
