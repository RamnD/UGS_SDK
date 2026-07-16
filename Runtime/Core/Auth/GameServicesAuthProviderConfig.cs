using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Optional platform provider parameters for UGS Authentication linking.
/// Keys in projects are often supplied via ScriptableObject, Remote Config, or CI — collect them in one object and pass to the builder via WithAuthProviderCredentials.
/// </summary>
public sealed class GameServicesAuthProviderConfig
{
    public static GameServicesAuthProviderConfig Empty => new GameServicesAuthProviderConfig();

    /// <summary>
    /// Google Play Games OAuth Web Client Id (type Web application).
    /// Empty — platform methods should treat as "key not provided".
    /// </summary>
    public string GooglePlayGamesOAuthWebClientId { get; set; }

    /// <summary>
    /// Apple Services ID configured in Apple Developer + UGS Dashboard (SIWA).
    /// Used for diagnostics; native token fetch is provided via <see cref="RequestAppleIdentityTokenAsync"/>.
    /// </summary>
    public string AppleServicesId { get; set; }

    /// <summary>
    /// Game-supplied Apple identity token (JWT) bridge for SignIn/Link with Apple (SIWA).
    /// Optional — prefer <see cref="RequestAppleGameCenterCredentialsAsync"/> for games.
    /// </summary>
    public Func<CancellationToken, Task<string>> RequestAppleIdentityTokenAsync { get; set; }

    /// <summary>
    /// Game-supplied Apple Game Center credentials bridge (GameKit FetchItems → UGS).
    /// Primary iOS identity for games.
    /// </summary>
    public Func<CancellationToken, Task<AppleGameCenterCredentials>> RequestAppleGameCenterCredentialsAsync { get; set; }
}
