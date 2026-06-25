/// <summary>
/// Leaderboard row. Portable DTO — independent of any specific SDK.
/// </summary>
public readonly struct LeaderboardEntry
{
    /// <summary>Unique player ID within the auth SDK.</summary>
    public readonly string PlayerId;

    /// <summary>Display name. May be empty if not set.</summary>
    public readonly string PlayerName;

    /// <summary>Rank in the table (1-based). 1 = first place.</summary>
    public readonly int Rank;

    /// <summary>
    /// Score in server format (double). For UI display, convert via
    /// <c>ScoreConverter.ToDisplayScore(Score)</c>.
    /// </summary>
    public readonly double Score;

    public LeaderboardEntry(string playerId, string playerName, int rank, double score)
    {
        PlayerId   = playerId;
        PlayerName = playerName;
        Rank       = rank;
        Score      = score;
    }
}
