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
    Task FetchAsync(CancellationToken cancellationToken = default);

    bool HasKey(string key);

    string GetString(string key, string defaultValue = "");

    /// <summary>JSON-значение ключа (для type=json в Dashboard). Не использовать GetString для JSON-объектов.</summary>
    string GetJson(string key, string defaultValue = "{}");

    bool GetBool(string key, bool defaultValue = false);

    int GetInt(string key, int defaultValue = 0);

    float GetFloat(string key, float defaultValue = 0f);
}
