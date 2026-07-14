using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.RemoteConfig;
using UnityEngine;

/// <summary>
/// <see cref="IRemoteConfigService"/> via Unity Gaming Services Remote Config.
/// </summary>
public sealed class UGSRemoteConfigService : IRemoteConfigService
{
    readonly RemoteConfigCache _cache = new RemoteConfigCache();
    bool _hasLiveConfig;

    /// <inheritdoc/>
    public bool IsReady { get; private set; }

    /// <inheritdoc/>
    public bool UsedCacheOnly { get; private set; }

    /// <inheritdoc/>
    public async Task FetchAsync(CancellationToken cancellationToken = default)
    {
        UsedCacheOnly = false;
        _hasLiveConfig = false;

        if (!NetworkStatus.IsOnline)
        {
            LoadCacheOnly();
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RemoteConfigService.Instance.FetchConfigsAsync(
                new RemoteConfigUserAttributes(),
                new RemoteConfigAppAttributes());

            cancellationToken.ThrowIfCancellationRequested();

            RefreshCacheFromAppConfig();
            _hasLiveConfig = true;
            IsReady = true;
            Debug.Log("[RemoteConfig] Fetch completed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RemoteConfig] Fetch failed, using cache if available: {ex.Message}");
            LoadCacheOnly();

            if (!IsReady)
                throw new RemoteConfigOperationException("Failed to fetch Remote Config and no cache is available.", ex);
        }
    }

    /// <inheritdoc/>
    public bool HasKey(string key)
    {
        if (TryGetLiveAppConfig(out RuntimeConfig appConfig) && appConfig.HasKey(key))
            return true;

        return _cache.HasKey(key);
    }

    /// <inheritdoc/>
    public string GetString(string key, string defaultValue = "")
    {
        if (TryGetLiveAppConfig(out RuntimeConfig appConfig) && appConfig.HasKey(key))
            return appConfig.GetString(key, defaultValue);

        return _cache.TryGetString(key, out string cached) ? cached : defaultValue;
    }

    /// <inheritdoc/>
    public string GetJson(string key, string defaultValue = "{}")
    {
        if (TryGetLiveAppConfig(out RuntimeConfig appConfig) && appConfig.HasKey(key))
            return appConfig.GetJson(key, defaultValue);

        return _cache.TryGetString(key, out string cached) && !string.IsNullOrEmpty(cached)
            ? cached
            : defaultValue;
    }

    /// <inheritdoc/>
    public bool GetBool(string key, bool defaultValue = false)
    {
        if (TryGetLiveAppConfig(out RuntimeConfig appConfig) && appConfig.HasKey(key))
            return appConfig.GetBool(key, defaultValue);

        if (!_cache.TryGetString(key, out string raw))
            return defaultValue;

        return bool.TryParse(raw, out bool value) ? value : defaultValue;
    }

    /// <inheritdoc/>
    public int GetInt(string key, int defaultValue = 0)
    {
        if (TryGetLiveAppConfig(out RuntimeConfig appConfig) && appConfig.HasKey(key))
            return appConfig.GetInt(key, defaultValue);

        if (!_cache.TryGetString(key, out string raw))
            return defaultValue;

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : defaultValue;
    }

    /// <inheritdoc/>
    public float GetFloat(string key, float defaultValue = 0f)
    {
        if (TryGetLiveAppConfig(out RuntimeConfig appConfig) && appConfig.HasKey(key))
            return appConfig.GetFloat(key, defaultValue);

        if (!_cache.TryGetString(key, out string raw))
            return defaultValue;

        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : defaultValue;
    }

    void LoadCacheOnly()
    {
        _cache.Load();
        UsedCacheOnly = true;
        _hasLiveConfig = false;
        IsReady = _cache.HasEntries;
    }

    void RefreshCacheFromAppConfig()
    {
        RuntimeConfig appConfig = RemoteConfigService.Instance.appConfig;
        if (appConfig == null)
            return;

        string[] keys = appConfig.GetKeys();
        var values = new Dictionary<string, string>(keys?.Length ?? 0, StringComparer.OrdinalIgnoreCase);

        if (keys != null)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (string.IsNullOrEmpty(key))
                    continue;

                values[key] = ReadConfigValue(appConfig, key);
            }
        }

        _cache.ReplaceAll(values);
        IsReady = true;
    }

    static string ReadConfigValue(RuntimeConfig appConfig, string key)
    {
        string stringValue = appConfig.GetString(key, string.Empty);
        if (!string.IsNullOrEmpty(stringValue))
            return stringValue;

        if (!appConfig.HasKey(key))
            return string.Empty;

        string jsonValue = appConfig.GetJson(key, string.Empty);
        return !string.IsNullOrEmpty(jsonValue) ? jsonValue : stringValue;
    }

    bool TryGetLiveAppConfig(out RuntimeConfig appConfig)
    {
        appConfig = _hasLiveConfig ? RemoteConfigService.Instance.appConfig : null;
        return appConfig != null;
    }

    struct RemoteConfigUserAttributes { }

    struct RemoteConfigAppAttributes { }
}
