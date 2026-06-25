using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Mock-реализация <see cref="ILeaderboardService"/>.
/// </summary>
public sealed class MockLeaderboardService : ILeaderboardService
{
    private const string MockPlayerId = "mock-player-000";

    private readonly Dictionary<string, List<LeaderboardEntry>> _tables = new();

    /// <inheritdoc/>
    public Task SubmitScoreAsync(string leaderboardId, double score,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var table  = GetOrCreateTable(leaderboardId);
        var idx    = table.FindIndex(e => e.PlayerId == MockPlayerId);
        var entry  = new LeaderboardEntry(MockPlayerId, "MockPlayer", 0, score);

        if (idx >= 0) table[idx] = entry;
        else          table.Add(entry);

        RebuildRanks(table);
        Debug.Log($"[Mock Leaderboard] SubmitScore '{leaderboardId}': {score}");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<LeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int count = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var table  = GetOrCreateTable(leaderboardId);
        IReadOnlyList<LeaderboardEntry> result = table.Take(count).ToList();
        Debug.Log($"[Mock Leaderboard] GetTopScores '{leaderboardId}': {result.Count} entries.");
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<LeaderboardEntry?> GetPlayerEntryAsync(string leaderboardId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var table = GetOrCreateTable(leaderboardId);
        var entry = table.FirstOrDefault(e => e.PlayerId == MockPlayerId);

        LeaderboardEntry? result = entry.PlayerId == MockPlayerId ? entry : (LeaderboardEntry?)null;

        Debug.Log(result.HasValue
            ? $"[Mock Leaderboard] Player found: rank {result.Value.Rank}, score {result.Value.Score}"
            : $"[Mock Leaderboard] Player not in '{leaderboardId}'.");

        return Task.FromResult(result);
    }

    private List<LeaderboardEntry> GetOrCreateTable(string leaderboardId)
    {
        if (!_tables.TryGetValue(leaderboardId, out var table))
        {
            table              = new List<LeaderboardEntry>();
            _tables[leaderboardId] = table;
        }
        return table;
    }

    private static void RebuildRanks(List<LeaderboardEntry> table)
    {
        table.Sort((a, b) => b.Score.CompareTo(a.Score));
        for (var i = 0; i < table.Count; i++)
        {
            var e = table[i];
            table[i] = new LeaderboardEntry(e.PlayerId, e.PlayerName, i + 1, e.Score);
        }
    }
}
