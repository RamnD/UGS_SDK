/// <summary>
/// Причина отказа при установке никнейма.
/// Возвращается из <see cref="IAuthService.ValidatePlayerName"/> (клиентская проверка)
/// и из <see cref="IAuthService.SetPlayerNameAsync"/> (полный цикл включая сервер).
/// <para>
/// null (nullable) означает успех — имя принято.
/// SDK намеренно не содержит локализованных строк — маппинг на UI-текст выполняет игра.
/// </para>
/// </summary>
public enum NameValidationError
{
    // ── Клиентская валидация (ValidatePlayerName) ─────────────────────────────

    /// <summary>Строка null, пустая или состоит только из пробелов.</summary>
    Empty,

    /// <summary>Длина меньше минимально допустимой (3 символа).</summary>
    TooShort,

    /// <summary>Длина превышает максимально допустимую (50 символов).</summary>
    TooLong,

    /// <summary>Содержит недопустимый символ (разрешены: буквы, цифры, пробел, -, _, .).</summary>
    InvalidCharacter,

    /// <summary>Совпадение с бан-листом (<see cref="NameValidatorConfig"/>).</summary>
    Profanity,

    // ── Серверные / сетевые ошибки (SetPlayerNameAsync) ──────────────────────

    /// <summary>
    /// Игрок не авторизован. Нужно сначала вызвать <see cref="IAuthService.SignInAsync"/>.
    /// </summary>
    NotSignedIn,

    /// <summary>
    /// Сервер UGS отклонил имя (HTTP 422 / error code 10009).
    /// Имя прошло клиентскую валидацию, но нарушает серверные ограничения формата.
    /// </summary>
    ServerRejected,

    /// <summary>
    /// Сетевая ошибка или непредвиденное исключение при обращении к серверу.
    /// Следует предложить игроку повторить попытку.
    /// </summary>
    NetworkError,
}
