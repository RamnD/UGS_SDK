using System;

/// <summary>
/// Classifies <see cref="AuthPlatform"/> for UI / product (game services vs portable cloud).
/// </summary>
public enum AuthIdentityLayer
{
    None,
    /// <summary>Platform game services: Game Center / Google Play Games.</summary>
    GameService,
    /// <summary>UGS-native portable IdP: Apple SIWA, Google OpenID, Facebook.</summary>
    CloudNative,
    /// <summary>Dashboard-registered OpenID Connect IdP.</summary>
    CloudOidc,
}

/// <summary>Helpers for <see cref="AuthPlatform"/>.</summary>
public static class AuthPlatformKind
{
    public static AuthIdentityLayer GetLayer(AuthPlatform platform) =>
        platform switch
        {
            AuthPlatform.AppleGameCenter or AuthPlatform.GooglePlayGames => AuthIdentityLayer.GameService,
            AuthPlatform.Apple or AuthPlatform.Google or AuthPlatform.Facebook => AuthIdentityLayer.CloudNative,
            AuthPlatform.OpenIdConnect => AuthIdentityLayer.CloudOidc,
            _ => AuthIdentityLayer.None,
        };

    public static bool IsGameService(AuthPlatform platform) =>
        GetLayer(platform) == AuthIdentityLayer.GameService;

    public static bool IsCloudIdentity(AuthPlatform platform)
    {
        AuthIdentityLayer layer = GetLayer(platform);
        return layer == AuthIdentityLayer.CloudNative || layer == AuthIdentityLayer.CloudOidc;
    }

    /// <summary>
    /// UGS Authentication external identity type id (PlayerInfo.Identities[].TypeId),
    /// or empty for Anonymous / unknown.
    /// For <see cref="AuthPlatform.OpenIdConnect"/> use the configured IdP name
    /// (often <c>oidc-&lt;name&gt;</c> — confirm in Dashboard).
    /// </summary>
    public static string GetExternalIdTypeId(AuthPlatform platform, string openIdConnectIdProviderName = null) =>
        platform switch
        {
            AuthPlatform.AppleGameCenter => "apple-game-center",
            AuthPlatform.GooglePlayGames => "google-play-games",
            AuthPlatform.Apple => "apple",
            AuthPlatform.Google => "google",
            AuthPlatform.Facebook => "facebook",
            AuthPlatform.OpenIdConnect => string.IsNullOrWhiteSpace(openIdConnectIdProviderName)
                ? string.Empty
                : openIdConnectIdProviderName.Trim(),
            _ => string.Empty,
        };

    public static bool IsLinkable(AuthPlatform platform) =>
        platform != AuthPlatform.Anonymous;
}
