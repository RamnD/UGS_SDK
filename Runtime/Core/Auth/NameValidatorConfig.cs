using System.Text.RegularExpressions;

/// <summary>
/// Конфигурация антицензора для валидации никнеймов.
/// Передаётся в SDK при инициализации через WithNameValidator, WithProfanityFilter(string[]) или WithProfanityFilter(Regex).
/// <para>
/// SDK намеренно не содержит встроенного бан-листа — проект сам решает что запрещено.
/// </para>
/// </summary>
public sealed class NameValidatorConfig
{
    /// <summary>Пустая конфигурация — никакие слова не запрещены.</summary>
    public static readonly NameValidatorConfig Empty = new NameValidatorConfig();

    /// <summary>
    /// Список запрещённых слов/подстрок (проверка без учёта регистра).
    /// Если хотя бы одно встречается в имени — вернётся <see cref="NameValidationError.Profanity"/>.
    /// </summary>
    public string[] BannedWords { get; }

    /// <summary>
    /// Регулярное выражение запрещённых паттернов.
    /// Если совпадает с именем — вернётся <see cref="NameValidationError.Profanity"/>.
    /// Проверяется после <see cref="BannedWords"/>.
    /// </summary>
    public Regex BannedPattern { get; }

    /// <summary>Создаёт пустую конфигурацию (антицензор отключён).</summary>
    public NameValidatorConfig()
    {
        BannedWords   = System.Array.Empty<string>();
        BannedPattern = null;
    }

    /// <summary>Создаёт конфигурацию с запрещёнными словами и/или паттерном.</summary>
    /// <param name="bannedWords">Список запрещённых слов. Null равнозначен пустому массиву.</param>
    /// <param name="bannedPattern">Regex запрещённых паттернов. Null — не применяется.</param>
    public NameValidatorConfig(string[] bannedWords = null, Regex bannedPattern = null)
    {
        BannedWords   = bannedWords ?? System.Array.Empty<string>();
        BannedPattern = bannedPattern;
    }
}
