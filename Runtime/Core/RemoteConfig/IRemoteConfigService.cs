using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Remote Config service. Abstracted from the concrete backend.
/// <para>Network errors → <see cref="RemoteConfigOperationException"/>.</para>
/// </summary>
public interface IRemoteConfigService
{
    /// <summary>True when config is available (remote fetch and/or disk cache).</summary>
    bool IsReady { get; }

    /// <summary>True when the latest load used only the local cache (offline or fetch fallback).</summary>
    bool UsedCacheOnly { get; }

    /// <summary>
    /// Fetches config from UGS when online. Falls back to the disk cache when offline or on failure.
    /// </summary>
    /// <param name="cancellationToken">Cancels the fetch await.</param>
    Task FetchAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the key exists in the loaded config (remote or cache).</summary>
    /// <param name="key">Remote Config key from the Dashboard.</param>
    bool HasKey(string key);

    /// <summary>Reads a string value, or <paramref name="defaultValue"/> when missing / not ready.</summary>
    string GetString(string key, string defaultValue = "");

    /// <summary>
    /// Raw JSON string for Dashboard keys with type <c>json</c>.
    /// Do not use <see cref="GetString"/> for JSON objects — it may not preserve structure.
    /// </summary>
    string GetJson(string key, string defaultValue = "{}");

    /// <summary>Reads a bool value, or <paramref name="defaultValue"/> when missing / not ready.</summary>
    bool GetBool(string key, bool defaultValue = false);

    /// <summary>Reads an int value, or <paramref name="defaultValue"/> when missing / not ready.</summary>
    int GetInt(string key, int defaultValue = 0);

    /// <summary>Reads a float value, or <paramref name="defaultValue"/> when missing / not ready.</summary>
    float GetFloat(string key, float defaultValue = 0f);
}
