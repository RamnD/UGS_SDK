/// <summary>
/// Запись в таблице лидеров. Portable DTO — не зависит от конкретного SDK.
/// </summary>
public readonly struct LeaderboardEntry
{
    /// <summary>Уникальный ID игрока в рамках SDK авторизации.</summary>
    public readonly string PlayerId;

    /// <summary>Отображаемое имя игрока. Может быть пустым если не установлено.</summary>
    public readonly string PlayerName;

    /// <summary>Место в таблице (1-based). 1 = первое место.</summary>
    public readonly int Rank;

    /// <summary>
    /// Счёт в формате сервера (double). Для отображения в UI конвертируйте через
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
