using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Optional platform provider parameters for UGS Authentication linking.
/// Keys / native token fetchers are supplied by the game via ScriptableObject, Remote Config, or CI —
/// collect them here and pass to the builder via <c>WithAuthProviderCredentials</c>.
/// </summary>
public sealed class GameServicesAuthProviderConfig
{
    /// <summary>Empty config (no OAuth ids, no credential bridges).</summary>
    public static GameServicesAuthProviderConfig Empty => new GameServicesAuthProviderConfig();

    /// <summary>
    /// Google Play Games OAuth Web Client Id (type Web application).
    /// Empty — platform methods should treat as "key not provided".
    /// </summary>
    public string GooglePlayGamesOAuthWebClientId { get; set; }

    /// <summary>
    /// Google OpenID Web Client Id (diagnostics / Dashboard setup).
    /// Token fetch is via <see cref="RequestGoogleIdTokenAsync"/>.
    /// </summary>
    public string GoogleOpenIdWebClientId { get; set; }

    /// <summary>
    /// Apple Services ID configured in Apple Developer + UGS Dashboard (SIWA).
    /// Used for diagnostics; native token fetch is provided via <see cref="RequestAppleIdentityTokenAsync"/>.
    /// </summary>
    public string AppleServicesId { get; set; }

    /// <summary>
    /// Facebook App Id (diagnostics). Token via <see cref="RequestFacebookAccessTokenAsync"/>.
    /// </summary>
    public string FacebookAppId { get; set; }

    /// <summary>
    /// OpenID Connect Id Provider name as registered in UGS Dashboard
    /// (used with <see cref="AuthPlatform.OpenIdConnect"/>).
    /// </summary>
    public string OpenIdConnectIdProviderName { get; set; }

    /// <summary>
    /// Game-supplied Apple identity token (JWT) bridge for SignIn/Link with Apple (SIWA).
    /// Supported on iOS and Android when the game wires a native/web SiWA flow.
    /// </summary>
    public Func<CancellationToken, Task<string>> RequestAppleIdentityTokenAsync { get; set; }

    /// <summary>
    /// Game-supplied Apple Game Center credentials bridge (GameKit FetchItems → UGS).
    /// Primary iOS game-service identity.
    /// </summary>
    public Func<CancellationToken, Task<AppleGameCenterCredentials>> RequestAppleGameCenterCredentialsAsync { get; set; }

    /// <summary>
    /// Game-supplied Google OpenID id_token (not GPGS server auth code).
    /// </summary>
    public Func<CancellationToken, Task<string>> RequestGoogleIdTokenAsync { get; set; }

    /// <summary>
    /// Game-supplied Facebook access token (USER token type for UGS).
    /// </summary>
    public Func<CancellationToken, Task<string>> RequestFacebookAccessTokenAsync { get; set; }

    /// <summary>
    /// Game-supplied OpenID Connect id_token for <see cref="OpenIdConnectIdProviderName"/>.
    /// </summary>
    public Func<CancellationToken, Task<string>> RequestOpenIdConnectIdTokenAsync { get; set; }
}
