/// <summary>
/// Reason a player name was rejected.
/// Returned from <see cref="IAuthService.ValidatePlayerName"/> (client-side check)
/// and from <see cref="IAuthService.SetPlayerNameAsync"/> (full flow including server).
/// <para>
/// null (nullable) means success — name accepted.
/// The SDK intentionally has no localized strings — map to UI text in your game.
/// </para>
/// </summary>
public enum NameValidationError
{
    // ── Client validation (ValidatePlayerName) ─────────────────────────────

    /// <summary>String is null, empty, or whitespace only.</summary>
    Empty,

    /// <summary>Length below minimum (3 characters).</summary>
    TooShort,

    /// <summary>Length exceeds maximum (50 characters).</summary>
    TooLong,

    /// <summary>Contains an invalid character (allowed: letters, digits, space, -, _, .).</summary>
    InvalidCharacter,

    /// <summary>Matches the ban list (<see cref="NameValidatorConfig"/>).</summary>
    Profanity,

    // ── Server / network errors (SetPlayerNameAsync) ──────────────────────

    /// <summary>
    /// Player is not signed in. Call <see cref="IAuthService.SignInAsync"/> first.
    /// </summary>
    NotSignedIn,

    /// <summary>
    /// UGS server rejected the name (HTTP 422 / error code 10009).
    /// Name passed client validation but violates server format rules.
    /// </summary>
    ServerRejected,

    /// <summary>
    /// Network error or unexpected exception when contacting the server.
    /// Prompt the player to retry.
    /// </summary>
    NetworkError,
}
