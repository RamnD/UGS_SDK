/// <summary>
/// Result of <see cref="IAuthService.LinkWithAccountAsync"/>.
/// </summary>
public enum AccountLinkResult
{
    /// <summary>Platform identity linked to the current (usually anonymous) player.</summary>
    Linked,

    /// <summary>
    /// Platform identity was already tied to another UGS player.
    /// SDK signed out the current session and signed into that existing player
    /// (typical after reinstall). Reload Cloud Save / Economy on the game side.
    /// </summary>
    SignedIntoExisting,

    /// <summary>Not signed in when link was requested.</summary>
    NotSignedIn,

    /// <summary>User cancelled (e.g. OperationCanceledException).</summary>
    Cancelled,

    /// <summary>Link / recover sign-in failed for another reason.</summary>
    Failed,
}
