using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Disk-backed key/value cache for Remote Config.
/// Values are stored as strings and parsed on read.
/// </summary>
internal sealed class RemoteConfigCache
{
    const string PrefsKey = "remote_config_cached_values";

    readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool HasEntries => _values.Count > 0;

    public void Load()
    {
        _values.Clear();

        string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return;

        CachePayload payload = JsonUtility.FromJson<CachePayload>(json);
        if (payload?.entries == null)
            return;

        for (int i = 0; i < payload.entries.Count; i++)
        {
            CacheEntry entry = payload.entries[i];
            if (entry == null || string.IsNullOrEmpty(entry.key))
                continue;

            _values[entry.key] = entry.value ?? string.Empty;
        }
    }

    public void ReplaceAll(IReadOnlyDictionary<string, string> values)
    {
        _values.Clear();

        if (values == null)
        {
            Save();
            return;
        }

        foreach (var pair in values)
        {
            if (string.IsNullOrEmpty(pair.Key))
                continue;

            _values[pair.Key] = pair.Value ?? string.Empty;
        }

        Save();
    }

    public bool HasKey(string key) =>
        !string.IsNullOrEmpty(key) && _values.ContainsKey(key);

    public bool TryGetString(string key, out string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            value = string.Empty;
            return false;
        }

        return _values.TryGetValue(key, out value);
    }

    public void Save()
    {
        var payload = new CachePayload();
        foreach (var pair in _values)
        {
            payload.entries.Add(new CacheEntry
            {
                key = pair.Key,
                value = pair.Value,
            });
        }

        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(payload));
        PlayerPrefs.Save();
    }

    [Serializable]
    sealed class CacheEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    sealed class CachePayload
    {
        public List<CacheEntry> entries = new List<CacheEntry>();
    }
}
