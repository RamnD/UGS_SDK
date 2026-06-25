using System;

/// <summary>
/// Ошибка операции таблицы лидеров (сеть, UGS или неверная конфигурация).
/// Некоторые сценарии «нет строки игрока» провайдер отдаёт как null или 404-деградация — см. реализацию UGSLeaderboardService.
/// </summary>
public sealed class LeaderboardOperationException : Exception
{
    public LeaderboardOperationException(string message) : base(message) { }

    public LeaderboardOperationException(string message, Exception innerException) : base(message, innerException) { }
}
