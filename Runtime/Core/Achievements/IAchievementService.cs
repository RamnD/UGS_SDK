using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Portable achievement service abstraction.
/// Achievement IDs are project-defined string constants; the SDK stores only generic progress state.
/// </summary>
public interface IAchievementService
{
    /// <summary>
    /// Returns the last known state for an achievement, or false if it has never been seen.
    /// </summary>
    bool TryGetState(string achievementId, out AchievementState state);

    /// <summary>
    /// Returns a snapshot of all known achievement states.
    /// </summary>
    IReadOnlyCollection<AchievementState> GetAllStates();

    /// <summary>
    /// Sets absolute progress and target for an achievement.
    /// Implementations may auto-unlock when <paramref name="currentProgress"/> reaches <paramref name="targetProgress"/>.
    /// </summary>
    Task SetProgressAsync(
        string achievementId,
        double currentProgress,
        double targetProgress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds progress to an achievement.
    /// Implementations may auto-unlock when the resulting progress reaches <paramref name="targetProgress"/>.
    /// </summary>
    Task IncrementProgressAsync(
        string achievementId,
        double deltaProgress,
        double targetProgress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an achievement as unlocked.
    /// </summary>
    Task UnlockAsync(string achievementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes any pending local state to the backing store if needed.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
