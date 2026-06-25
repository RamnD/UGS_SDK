using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Сервис аутентификации игрока.
/// Абстрагирован от конкретного SDK — поменяйте реализацию в билдере
/// без изменения остального кода.
/// </summary>
public interface IAuthService
{
    /// <summary>Авторизован ли игрок прямо сейчас.</summary>
    bool IsSignedIn { get; }

    /// <summary>
    /// Возвращает уникальный ID игрока в рамках SDK.
    /// Возвращает "unknown" если не авторизован.
    /// </summary>
    string GetPlayerId();

    /// <summary>
    /// Выполняет вход.
    /// Логика приоритетов (UGS): Anonymous → форсированный аноним;
    /// первый визит (нет session token) → аноним; повторный → метод из прошлой сессии.
    /// </summary>
    /// <param name="platform">Желаемая платформа. Может быть переопределена сохранённым методом.</param>
    /// <param name="cancellationToken">Отмена длительного входа или таймаута.</param>
    /// <returns>True если вход выполнен успешно.</returns>
    Task<bool> SignInAsync(AuthPlatform platform, CancellationToken cancellationToken = default);

    /// <summary>
    /// Привязывает анонимный аккаунт к платформенному (Google Play / Apple).
    /// Вызывать после обучения, когда игрок выбирает "Войти через Google/Apple".
    /// </summary>
    Task<bool> LinkWithAccountAsync(AuthPlatform platform, CancellationToken cancellationToken = default);

    /// <summary>
    /// Полный сброс: выход из SDK + удаление сохранённого метода авторизации.
    /// После вызова следующий <see cref="SignInAsync"/> создаст новый анонимный аккаунт.
    /// </summary>
    void Reset();

    /// <summary>
    /// Возвращает никнейм игрока из профиля UGS Authentication.
    /// Именно это значение видят все сервисы UGS — лидерборд, Cloud Save и др.
    /// Пустая строка если никнейм не установлен.
    /// </summary>
    string GetPlayerName();

    /// <summary>
    /// Устанавливает никнейм в профиле UGS Authentication.
    /// Все сервисы UGS (лидерборд и др.) увидят его автоматически.
    /// </summary>
    /// <returns>
    /// null — имя принято и сохранено на сервере.<br/>
    /// <see cref="NameValidationError"/> — причина отказа (клиентская или серверная).
    /// </returns>
    Task<NameValidationError?> SetPlayerNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Клиентская валидация имени: длина, символы, базовый список запрещённых слов.
    /// Вызывай в UI при каждом изменении поля ввода для live-фидбека.
    /// Серверная проверка UGS всё равно выполняется при <see cref="SetPlayerNameAsync"/>.
    /// </summary>
    /// <returns>
    /// null — имя валидно.
    /// <see cref="NameValidationError"/> — причина отказа. Локализацию выполняет игра.
    /// </returns>
    NameValidationError? ValidatePlayerName(string name);
}
