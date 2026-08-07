/// <summary>
/// Supported authentication strategies.
/// Intentionally distinct from <see cref="UnityEngine.RuntimePlatform"/> —
/// only what is wired in the auth layer.
/// </summary>
public enum AuthPlatform
{
    /// <summary>Anonymous sign-in — not tied to a platform. Default and used in the Editor.</summary>
    Anonymous,

    /// <summary>Google Play Games — Android game service (achievements / native Play identity).</summary>
    GooglePlayGames,

    /// <summary>
    /// Sign in with Apple (SIWA) — portable cloud identity (iOS + Android when token bridge is set).
    /// Cross-device cloud save bridge. Prefer alongside <see cref="Google"/> for portability.
    /// </summary>
    Apple,

    /// <summary>Apple Game Center — iOS game service. Pair to <see cref="GooglePlayGames"/> on Android.</summary>
    AppleGameCenter,

    /// <summary>
    /// Google OpenID (id_token) — portable cloud identity on iOS + Android.
    /// Distinct from <see cref="GooglePlayGames"/>.
    /// </summary>
    Google,

    /// <summary>Facebook Login — optional portable cloud identity (UGS-native).</summary>
    Facebook,

    /// <summary>
    /// Custom OpenID Connect IdP registered in UGS Dashboard (Discord, Snapchat, TikTok, X, …).
    /// Provider name + id_token come from <see cref="GameServicesAuthProviderConfig"/>.
    /// </summary>
    OpenIdConnect,
}
