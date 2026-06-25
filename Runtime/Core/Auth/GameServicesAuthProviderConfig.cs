/// <summary>
/// Optional platform provider parameters for UGS Authentication linking.
/// Keys in projects are often supplied via ScriptableObject, Remote Config, or CI — collect them in one object and pass to the builder via WithAuthProviderCredentials.
/// <para>
/// TODO: use identifiers in native GPGS / Sign in with Apple flows before calling the UGS SDK.
/// </para>
/// </summary>
public sealed class GameServicesAuthProviderConfig
{
    public static GameServicesAuthProviderConfig Empty => new GameServicesAuthProviderConfig();

    /// <summary>
    /// TODO(Google Play Games → UGS): Web Client Id / OAuth client if required by your GPGS + UGS setup
    /// (depends on Play Console and plugin configuration). Empty — platform methods should treat as "key not provided".
    /// </summary>
    public string GooglePlayGamesOAuthWebClientId { get; set; }

    /// <summary>
    /// TODO(Apple Sign-In → UGS): Services ID or other app identifier for Apple + UGS. Empty — Sign-In/link methods on iOS are unavailable (see Auth log).
    /// </summary>
    public string AppleServicesId { get; set; }
}
