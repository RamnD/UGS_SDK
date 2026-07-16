/// <summary>
/// Fresh GameKit verification payload for UGS Apple Game Center SignIn/Link.
/// Obtain via <c>GKLocalPlayer.Authenticate</c> + <c>FetchItems</c> (valid ~10 minutes).
/// </summary>
public sealed class AppleGameCenterCredentials
{
    public string Signature { get; set; }
    public string TeamPlayerId { get; set; }
    public string PublicKeyUrl { get; set; }
    public string Salt { get; set; }
    public ulong Timestamp { get; set; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Signature)
        && !string.IsNullOrWhiteSpace(TeamPlayerId)
        && !string.IsNullOrWhiteSpace(PublicKeyUrl)
        && !string.IsNullOrWhiteSpace(Salt)
        && Timestamp > 0;
}
