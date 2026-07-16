/// <summary>
/// Supported authentication strategies.
/// Intentionally distinct from <see cref="UnityEngine.RuntimePlatform"/> —
/// only what is actually implemented in the auth layer.
/// </summary>
public enum AuthPlatform
{
    /// <summary>Anonymous sign-in — not tied to a platform. Default and used in the Editor.</summary>
    Anonymous,

    /// <summary>Google Play Games — Android only. Requires GPGS SDK setup.</summary>
    GooglePlayGames,

    /// <summary>
    /// Sign in with Apple (SIWA) — iOS. Optional consumer identity.
    /// Prefer <see cref="AppleGameCenter"/> for games; keep SIWA for future/account flows.
    /// </summary>
    Apple,

    /// <summary>Apple Game Center — iOS primary gaming identity. Pair to Google Play Games on Android.</summary>
    AppleGameCenter,
}
