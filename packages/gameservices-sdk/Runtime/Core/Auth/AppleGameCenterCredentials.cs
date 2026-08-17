/// <summary>
/// Fresh GameKit verification payload for UGS Apple Game Center SignIn/Link.
/// Obtain via <c>GKLocalPlayer.Authenticate</c> + <c>FetchItems</c> (valid ~10 minutes).
/// Pass through <see cref="GameServicesAuthProviderConfig.RequestAppleGameCenterCredentialsAsync"/>.
/// </summary>
public sealed class AppleGameCenterCredentials
{
    /// <summary>Base64 signature from GameKit <c>FetchItems</c>.</summary>
    public string Signature { get; set; }

    /// <summary>Game Center team player id.</summary>
    public string TeamPlayerId { get; set; }

    /// <summary>URL of Apple's public key used to verify the signature.</summary>
    public string PublicKeyUrl { get; set; }

    /// <summary>Base64 salt from GameKit <c>FetchItems</c>.</summary>
    public string Salt { get; set; }

    /// <summary>Unix timestamp (seconds) from GameKit <c>FetchItems</c>.</summary>
    public ulong Timestamp { get; set; }

    /// <summary>True when all required fields are non-empty and <see cref="Timestamp"/> &gt; 0.</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Signature)
        && !string.IsNullOrWhiteSpace(TeamPlayerId)
        && !string.IsNullOrWhiteSpace(PublicKeyUrl)
        && !string.IsNullOrWhiteSpace(Salt)
        && Timestamp > 0;
}
