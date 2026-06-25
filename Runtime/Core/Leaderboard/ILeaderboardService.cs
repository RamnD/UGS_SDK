using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Leaderboard service. Abstracted from the concrete backend.
/// <para>
/// Leaderboard IDs are string constants from a project class (e.g. LeaderboardIds).
/// Score is passed as <c>double</c> (native UGS type).
/// </para>
/// <para>Network errors → <see cref="LeaderboardOperationException"/>.</para>
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Submits a run score to the server. Call after level/run completion.
    /// </summary>
    /// <exception cref="LeaderboardOperationException">Network, UGS, or configuration error.</exception>
    Task SubmitScoreAsync(string leaderboardId, double score, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the top-N leaderboard entries, sorted by score descending.
    /// An empty list is a valid "no entries" response. Errors throw.
    /// </summary>
    /// <exception cref="LeaderboardOperationException">Network, UGS, or configuration error.</exception>
    Task<IReadOnlyList<LeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int count = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current player's entry with their rank.
    /// Null if the player has not submitted a score or is not on the board.
    /// </summary>
    /// <exception cref="LeaderboardOperationException">Network, UGS, or configuration error.</exception>
    Task<LeaderboardEntry?> GetPlayerEntryAsync(string leaderboardId,
        CancellationToken cancellationToken = default);
}
