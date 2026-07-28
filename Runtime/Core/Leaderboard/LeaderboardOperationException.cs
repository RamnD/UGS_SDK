using System;

/// <summary>
/// Leaderboard operation failure (network, UGS, or misconfiguration).
/// Some "no player row" cases are returned as null or 404 degradation by the provider — see UGSLeaderboardService.
/// </summary>
public sealed class LeaderboardOperationException : Exception
{
    /// <summary>Creates a leaderboard failure with a diagnostic message.</summary>
    public LeaderboardOperationException(string message) : base(message) { }

    /// <summary>Creates a leaderboard failure wrapping a provider exception.</summary>
    public LeaderboardOperationException(string message, Exception innerException) : base(message, innerException) { }
}
