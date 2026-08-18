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
        if (!NetworkStatus.IsOnline)
            throw new LeaderboardOperationException(
                $"Cannot submit score for '{leaderboardId}' — device is offline.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await NetworkRequest.WithTimeout(
                LeaderboardsService.Instance.AddPlayerScoreAsync(leaderboardId, score),
                cancellationToken);
            NetworkStatus.ReportSuccess();
            AppLog.Info("Leaderboard", $"Score submitted: {leaderboardId} → {score}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (IsRecoverableTransport(e))
        {
            NetworkStatus.ReportFailure();
            AppLog.Error("Leaderboard", $"Submit failed '{leaderboardId}' (transport): {e.Message}");
            throw new LeaderboardOperationException(
                $"Failed to submit score for '{leaderboardId}'.", e);
        }
        catch (Exception e)
        {
            AppLog.Error("Leaderboard", $"Submit failed '{leaderboardId}': {e.Message}");
            throw new LeaderboardOperationException(
                $"Failed to submit score for '{leaderboardId}'.", e);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int count = 100,
        CancellationToken cancellationToken = default)
    {
        if (!NetworkStatus.IsOnline)
            return Array.Empty<LeaderboardEntry>();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await NetworkRequest.WithTimeout(
                LeaderboardsService.Instance.GetScoresAsync(
                    leaderboardId,
                    new GetScoresOptions { Limit = count }),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            NetworkStatus.ReportSuccess();

            var list = response.Results
                .Select(e => new LeaderboardEntry(e.PlayerId, e.PlayerName, e.Rank, e.Score))
                .ToList();
            return list;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (IsRecoverableTransport(e))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Leaderboard", $"GetTopScores failed '{leaderboardId}' (transport): {e.Message}");
            return Array.Empty<LeaderboardEntry>();
        }
        catch (Exception e)
        {
            AppLog.Error("Leaderboard", $"GetTopScores failed '{leaderboardId}': {e.Message}");
            throw new LeaderboardOperationException(
                $"Failed to load top scores for '{leaderboardId}'.", e);
        }
    }

    /// <inheritdoc/>
    public async Task<LeaderboardEntry?> GetPlayerEntryAsync(string leaderboardId,
        CancellationToken cancellationToken = default)
    {
        if (!NetworkStatus.IsOnline)
            return null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await NetworkRequest.WithTimeout(
                LeaderboardsService.Instance.GetPlayerScoreAsync(leaderboardId),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            NetworkStatus.ReportSuccess();
            return new LeaderboardEntry(entry.PlayerId, entry.PlayerName, entry.Rank, entry.Score);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (IsRecoverableTransport(e))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Leaderboard", $"GetPlayerScore failed '{leaderboardId}' (transport): {e.Message}");
            return null;
        }
        catch (Exception e)
        {
            if (IsMissingPlayerScore(e))
            {
                AppLog.Info("Leaderboard", $"Player has no row yet '{leaderboardId}'.");
                return null;
            }

            AppLog.Warn("Leaderboard", $"GetPlayerScore failed '{leaderboardId}': {e.Message}");
            throw new LeaderboardOperationException(
                $"Failed to get player entry for '{leaderboardId}'.", e);
        }
    }

    static bool IsRecoverableTransport(Exception exception)
    {
        for (Exception walk = exception; walk != null; walk = walk.InnerException)
        {
            if (walk is OperationCanceledException)
                return false;
            if (walk is TimeoutException)
                return true;
            if (walk is System.Net.Sockets.SocketException)
                return true;
            if (walk is System.Net.Http.HttpRequestException)
                return true;
        }

        return false;
    }

    static bool IsMissingPlayerScore(Exception exception)
    {
        for (Exception walk = exception; walk != null; walk = walk.InnerException)
        {
            if (walk is Unity.Services.Core.RequestFailedException requestFailed)
            {
                if (requestFailed.ErrorCode == 404)
                    return true;
            }
        }

        return false;
    }
}
