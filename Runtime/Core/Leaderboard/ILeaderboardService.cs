using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Сервис таблицы лидеров. Абстрагирован от конкретного бэкенда.
/// <para>
/// ID лидербордов — строковые константы из проектного класса (например LeaderboardIds).
/// Score передаётся как <c>double</c> (нативный тип UGS).
/// </para>
/// <para>Сетевые ошибки → <see cref="LeaderboardOperationException"/>.</para>
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Отправляет результат забега на сервер. Вызывать после завершения уровня/забега.
    /// </summary>
    /// <exception cref="LeaderboardOperationException">Сеть, UGS или конфигурация.</exception>
    Task SubmitScoreAsync(string leaderboardId, double score, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает топ-N записей лидерборда, отсортированных по убыванию счёта.
    /// Пустой список — легитимное «нет записей». Ошибки — исключением.
    /// </summary>
    /// <exception cref="LeaderboardOperationException">Сеть, UGS или конфигурация.</exception>
    Task<IReadOnlyList<LeaderboardEntry>> GetTopScoresAsync(string leaderboardId, int count = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает запись текущего игрока с его позицией в таблице.
    /// Null если игрок ещё не отправлял результат или отсутствует в таблице.
    /// </summary>
    /// <exception cref="LeaderboardOperationException">Сеть, UGS или конфигурация.</exception>
    Task<LeaderboardEntry?> GetPlayerEntryAsync(string leaderboardId,
        CancellationToken cancellationToken = default);
}
