using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Mock <see cref="IRemoteConfigService"/> with in-memory values.</summary>
public sealed class MockRemoteConfigService : IRemoteConfigService
{
    readonly Dictionary<string, string> _values = new Dictionary<string, string>();

    /// <inheritdoc/>
    public bool IsReady { get; private set; }

    /// <inheritdoc/>
    public bool UsedCacheOnly { get; private set; }

    /// <inheritdoc/>
    public Task FetchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UsedCacheOnly = false;
        IsReady = true;
        Debug.Log("[Mock RemoteConfig] Fetch completed (in-memory).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool HasKey(string key) =>
        !string.IsNullOrEmpty(key) && _values.ContainsKey(key);

    /// <inheritdoc/>
    public string GetString(string key, string defaultValue = "") =>
        HasKey(key) ? _values[key] : defaultValue;

    /// <inheritdoc/>
    public bool GetBool(string key, bool defaultValue = false)
    {
        if (!HasKey(key))
            return defaultValue;

        return bool.TryParse(_values[key], out bool value) ? value : defaultValue;
    }

    /// <inheritdoc/>
    public int GetInt(string key, int defaultValue = 0)
    {
        if (!HasKey(key))
            return defaultValue;

        return int.TryParse(_values[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : defaultValue;
    }

    /// <inheritdoc/>
    public float GetFloat(string key, float defaultValue = 0f)
    {
        if (!HasKey(key))
            return defaultValue;

        return float.TryParse(_values[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : defaultValue;
    }

    /// <summary>Sets a mock value for tests or editor bootstrap.</summary>
    public void SetValue(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
            return;

        _values[key] = value ?? string.Empty;
        IsReady = true;
    }
}
