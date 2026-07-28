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
    /// <param name="leaderboardId">Dashboard leaderboard id (project string constant).</param>
    /// <param name="score">Score in server units (<c>double</c> — native UGS type).</param>
    /// <param name="cancellationToken">Cancels the submit await.</param>
    /// <exception cref="LeaderboardOperationException">Network, UGS, or configuration error.</exception>
    Task SubmitScoreAsync(string leaderboardId, double score, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the top-N leaderboard entries, sorted by score descending.
    /// An empty list is a valid "no entries" response. Errors throw.
    /// </summary>
    /// <param name="leaderboardId">Dashboard leaderboard id.</param>
    /// <param name="count">Maximum number of rows to return (clamped by the provider).</param>
    /// <param name="cancellationToken">Cancels the fetch await.</param>
    /// <returns>Top rows; never null (may be empty).</returns>
    /// <exception cref="LeaderboardOperationException">Network, UGS, or configuration error.</exception>
    Task<IReadOnlyList<LeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int count = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current player's entry with their rank.
    /// Null if the player has not submitted a score or is not on the board.
    /// </summary>
    /// <param name="leaderboardId">Dashboard leaderboard id.</param>
    /// <param name="cancellationToken">Cancels the fetch await.</param>
    /// <returns>Player row, or null when not ranked.</returns>
    /// <exception cref="LeaderboardOperationException">Network, UGS, or configuration error.</exception>
    Task<LeaderboardEntry?> GetPlayerEntryAsync(string leaderboardId,
        CancellationToken cancellationToken = default);
}
