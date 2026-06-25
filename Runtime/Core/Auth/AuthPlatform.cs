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

    /// <summary>Apple Sign-In — iOS only. Requires a native plugin to obtain identityToken.</summary>
    Apple
}
