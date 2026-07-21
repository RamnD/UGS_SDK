using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Player authentication service.
/// Abstracted from the concrete SDK — swap the implementation in the builder
/// without changing the rest of your code.
/// </summary>
public interface IAuthService
{
    /// <summary>Whether the player is signed in right now.</summary>
    bool IsSignedIn { get; }

    /// <summary>
    /// Returns the player's unique ID within the SDK.
    /// Returns "unknown" if not signed in.
    /// </summary>
    string GetPlayerId();

    /// <summary>
    /// Signs in.
    /// Priority logic (UGS): Anonymous → forced anonymous;
    /// first visit (no session token) → anonymous; return visit → method from the previous session.
    /// </summary>
    /// <param name="platform">Desired platform. May be overridden by a saved sign-in method.</param>
    /// <param name="cancellationToken">Cancels a long-running sign-in or timeout.</param>
    /// <returns>True if sign-in succeeded.</returns>
    Task<bool> SignInAsync(AuthPlatform platform, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links an anonymous account to a platform account (Google Play / Apple / Game Center).
    /// Call after onboarding when the player chooses "Sign in with Google/Apple".
    /// </summary>
    /// <returns>
    /// <see cref="AccountLinkResult.Linked"/> on a normal link;
    /// <see cref="AccountLinkResult.SignedIntoExisting"/> when the external ID was already
    /// tied to another UGS player (recover via SignIn — typical after reinstall).
    /// </returns>
    Task<AccountLinkResult> LinkWithAccountAsync(AuthPlatform platform, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full reset: sign out of the SDK + clear the saved auth method.
    /// The next <see cref="SignInAsync"/> creates a new anonymous account.
    /// </summary>
    void Reset();

    /// <summary>
    /// Returns the player's nickname from the UGS Authentication profile.
    /// This is what all UGS services see — leaderboards, Cloud Save, etc.
    /// Empty string if no nickname is set.
    /// </summary>
    string GetPlayerName();

    /// <summary>
    /// Sets the nickname in the UGS Authentication profile.
    /// All UGS services (leaderboards, etc.) pick it up automatically.
    /// </summary>
    /// <returns>
    /// null — name accepted and saved on the server.<br/>
    /// <see cref="NameValidationError"/> — rejection reason (client or server).
    /// </returns>
    Task<NameValidationError?> SetPlayerNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Client-side name validation: length, characters, basic banned-word list.
    /// Call from UI on every input change for live feedback.
    /// UGS server validation still runs in <see cref="SetPlayerNameAsync"/>.
    /// </summary>
    /// <returns>
    /// null — name is valid.
    /// <see cref="NameValidationError"/> — rejection reason. Localization is the game's responsibility.
    /// </returns>
    NameValidationError? ValidatePlayerName(string name);
}
