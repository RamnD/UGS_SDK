using System.Text.RegularExpressions;

/// <summary>
/// Profanity-filter configuration for nickname validation.
/// Passed to the SDK at init via WithNameValidator, WithProfanityFilter(string[]) or WithProfanityFilter(Regex).
/// <para>
/// The SDK intentionally has no built-in ban list — the project decides what is forbidden.
/// </para>
/// </summary>
public sealed class NameValidatorConfig
{
    /// <summary>Empty configuration — no words banned.</summary>
    public static readonly NameValidatorConfig Empty = new NameValidatorConfig();

    /// <summary>
    /// Banned words/substrings (case-insensitive check).
    /// If any match the name — returns <see cref="NameValidationError.Profanity"/>.
    /// </summary>
    public string[] BannedWords { get; }

    /// <summary>
    /// Regex for banned patterns.
    /// If it matches the name — returns <see cref="NameValidationError.Profanity"/>.
    /// Checked after <see cref="BannedWords"/>.
    /// </summary>
    public Regex BannedPattern { get; }

    /// <summary>Creates an empty configuration (profanity filter disabled).</summary>
    public NameValidatorConfig()
    {
        BannedWords   = System.Array.Empty<string>();
        BannedPattern = null;
    }

    /// <summary>Creates a configuration with banned words and/or a pattern.</summary>
    /// <param name="bannedWords">Banned word list. Null is treated as an empty array.</param>
    /// <param name="bannedPattern">Regex for banned patterns. Null — not applied.</param>
    public NameValidatorConfig(string[] bannedWords = null, Regex bannedPattern = null)
    {
        BannedWords   = bannedWords ?? System.Array.Empty<string>();
        BannedPattern = bannedPattern;
    }
}
