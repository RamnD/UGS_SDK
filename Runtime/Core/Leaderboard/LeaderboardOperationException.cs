using System;

/// <summary>
/// Leaderboard operation failure (network, UGS, or misconfiguration).
/// Some "no player row" cases are returned as null or 404 degradation by the provider — see UGSLeaderboardService.
/// </summary>
public sealed class LeaderboardOperationException : Exception
{
    public LeaderboardOperationException(string message) : base(message) { }

    public LeaderboardOperationException(string message, Exception innerException) : base(message, innerException) { }
}
