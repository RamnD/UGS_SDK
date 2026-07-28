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
    /// <param name="achievementId">Project-defined achievement id.</param>
    /// <param name="state">Snapshot when found.</param>
    /// <returns>True when the achievement exists in the local/cloud cache.</returns>
    bool TryGetState(string achievementId, out AchievementState state);

    /// <summary>
    /// Returns a snapshot of all known achievement states.
    /// </summary>
    IReadOnlyCollection<AchievementState> GetAllStates();

    /// <summary>
    /// Sets absolute progress and target for an achievement.
    /// Implementations may auto-unlock when <paramref name="currentProgress"/> reaches <paramref name="targetProgress"/>.
    /// </summary>
    /// <param name="achievementId">Project-defined achievement id.</param>
    /// <param name="currentProgress">Absolute progress value to store.</param>
    /// <param name="targetProgress">Unlock threshold.</param>
    /// <param name="cancellationToken">Cancels the write await.</param>
    Task SetProgressAsync(
        string achievementId,
        double currentProgress,
        double targetProgress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds progress to an achievement.
    /// Implementations may auto-unlock when the resulting progress reaches <paramref name="targetProgress"/>.
    /// </summary>
    /// <param name="achievementId">Project-defined achievement id.</param>
    /// <param name="deltaProgress">Amount to add to current progress.</param>
    /// <param name="targetProgress">Unlock threshold.</param>
    /// <param name="cancellationToken">Cancels the write await.</param>
    Task IncrementProgressAsync(
        string achievementId,
        double deltaProgress,
        double targetProgress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an achievement as unlocked.
    /// </summary>
    /// <param name="achievementId">Project-defined achievement id.</param>
    /// <param name="cancellationToken">Cancels the write await.</param>
    Task UnlockAsync(string achievementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes any pending local state to the backing store if needed.
    /// </summary>
    /// <param name="cancellationToken">Cancels the flush await.</param>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears in-memory achievement state (does not delete Cloud Save).
    /// Call on account delete / switch before a new player session uses this service.
    /// </summary>
    void ClearLocalCache();
}
