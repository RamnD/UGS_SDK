using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds game-services ingest envelope objects from typed <see cref="IAnalyticsEvent"/> structs.
/// </summary>
public static class RamndAnalyticsEnvelopeBuilder
{
    const string InstallIdPrefsKey = "ramnd.analytics.install_id";

    /// <summary>Stable install id stored in PlayerPrefs (created once).</summary>
    public static string GetOrCreateInstallUserId()
    {
        string existing = PlayerPrefs.GetString(InstallIdPrefsKey, string.Empty);
        if (!string.IsNullOrEmpty(existing))
            return existing;

        string created = Guid.NewGuid().ToString("D");
        PlayerPrefs.SetString(InstallIdPrefsKey, created);
        PlayerPrefs.Save();
        return created;
    }

    public static string ResolvePlatformLabel()
    {
        RuntimePlatform platform = Application.platform;
        if (platform == RuntimePlatform.Android)
            return "ANDROID";
        if (platform == RuntimePlatform.IPhonePlayer)
            return "IOS";
        if (platform == RuntimePlatform.OSXEditor
            || platform == RuntimePlatform.WindowsEditor
            || platform == RuntimePlatform.LinuxEditor)
            return "EDITOR";
        if (platform == RuntimePlatform.OSXPlayer)
            return "OSX";
        if (platform == RuntimePlatform.WindowsPlayer)
            return "WINDOWS";
        if (platform == RuntimePlatform.LinuxPlayer)
            return "LINUX";
        if (platform == RuntimePlatform.WebGLPlayer)
            return "WEBGL";
        return platform.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// Flat event dictionary ready for JSON serialization (envelope + AnalyticsKey params).
    /// </summary>
    public static Dictionary<string, object> BuildEventObject<T>(
        T eventPayload,
        string userId,
        string platform,
        string playerId,
        DateTime? utcNow = null)
        where T : struct, IAnalyticsEvent
    {
        DateTime now = utcNow ?? DateTime.UtcNow;
        var row = new Dictionary<string, object>
        {
            ["event_id"] = Guid.NewGuid().ToString("D"),
            ["event_name"] = eventPayload.EventName,
            ["event_timestamp"] = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["user_id"] = userId ?? string.Empty,
            ["platform"] = platform ?? "UNKNOWN",
        };

        if (!string.IsNullOrEmpty(playerId))
            row["player_id"] = playerId;

        foreach (var kv in AnalyticsEventParams.ToDictionary(eventPayload))
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value == null)
                continue;
            // Do not let params overwrite envelope keys.
            if (row.ContainsKey(kv.Key))
                continue;
            row[kv.Key] = kv.Value;
        }

        return row;
    }
}
