using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;
using UnityEngine;

/// <summary>
/// <see cref="ILeaderboardService"/> implementation via Unity Gaming Services Leaderboards SDK 2.x.
/// </summary>
public sealed class UGSLeaderboardService : ILeaderboardService
{
    /// <inheritdoc/>
    public async Task SubmitScoreAsync(string leaderboardId, double score,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LeaderboardsService.Instance.AddPlayerScoreAsync(leaderboardId, score);
            Debug.Log($"[Leaderboard] Score submitted: {leaderboardId} → {score}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Leaderboard] Submit failed '{leaderboardId}': {e.Message}");
            throw new LeaderboardOperationException(
                $"Failed to submit score for '{leaderboardId}'.", e);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int count = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await LeaderboardsService.Instance.GetScoresAsync(
                leaderboardId,
                new GetScoresOptions { Limit = count });

            cancellationToken.ThrowIfCancellationRequested();

            var list = response.Results
                .Select(e => new LeaderboardEntry(e.PlayerId, e.PlayerName, e.Rank, e.Score))
                .ToList();
            return list;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Leaderboard] GetTopScores failed '{leaderboardId}': {e.Message}");
            throw new LeaderboardOperationException(
                $"Failed to load top scores for '{leaderboardId}'.", e);
        }
    }

    /// <inheritdoc/>
    public async Task<LeaderboardEntry?> GetPlayerEntryAsync(string leaderboardId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await LeaderboardsService.Instance.GetPlayerScoreAsync(leaderboardId);
            cancellationToken.ThrowIfCancellationRequested();
            return new LeaderboardEntry(entry.PlayerId, entry.PlayerName, entry.Rank, entry.Score);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            if (IsMissingPlayerScore(e))
            {
                Debug.Log($"[Leaderboard] Player has no row yet '{leaderboardId}'.");
                return null;
            }

            Debug.LogWarning($"[Leaderboard] GetPlayerScore failed '{leaderboardId}': {e.Message}");
            throw new LeaderboardOperationException(
                $"Failed to get player entry for '{leaderboardId}'.", e);
        }
    }

    static bool IsMissingPlayerScore(Exception exception)
    {
        for (Exception walk = exception; walk != null; walk = walk.InnerException)
        {
            if (walk is Unity.Services.Core.RequestFailedException requestFailed)
            {
                // UGS commonly uses 404 for "no score yet".
                if (requestFailed.ErrorCode == 404)
                    return true;
            }
        }

        return false;
    }
}
