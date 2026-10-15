using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Pending RamnD ingest envelopes. Callers serialize access; implementations do not.
/// </summary>
internal interface IRamndAnalyticsQueueStore
{
    List<Dictionary<string, object>> Load();

    void Save(IReadOnlyList<Dictionary<string, object>> events);
}

/// <summary>
/// Disk queue of frozen ingest envelopes. The file is stamped with the gateway base URL
/// so a staging queue is not replayed against production.
/// </summary>
internal sealed class RamndAnalyticsFileQueue : IRamndAnalyticsQueueStore
{
    const string FileName = "ramnd-analytics-queue.json";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        DateParseHandling = DateParseHandling.None,
        NullValueHandling = NullValueHandling.Ignore,
    };

    readonly string _path;
    readonly string _gateway;

    public RamndAnalyticsFileQueue(string directory, string gateway)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory is required.", nameof(directory));
        if (string.IsNullOrWhiteSpace(gateway))
            throw new ArgumentException("Gateway is required.", nameof(gateway));

        _path = Path.Combine(directory, FileName);
        _gateway = gateway.TrimEnd('/');
    }

    public static RamndAnalyticsFileQueue ForPersistentData(string gateway) =>
        new RamndAnalyticsFileQueue(Application.persistentDataPath, gateway);

    public List<Dictionary<string, object>> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new List<Dictionary<string, object>>();

            var snapshot = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_path), JsonSettings);
            if (snapshot == null)
                return new List<Dictionary<string, object>>();

            if (!string.Equals(snapshot.gateway, _gateway, StringComparison.Ordinal))
            {
                int dropped = snapshot.events?.Count ?? 0;
                if (dropped > 0)
                {
                    AppLog.Warn(
                        "Analytics",
                        $"Ramnd queue was written for gateway '{snapshot.gateway}' but this session uses " +
                        $"'{_gateway}' — ignoring {dropped} event(s) until the next save.");
                }

                return new List<Dictionary<string, object>>();
            }

            return snapshot.events ?? new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"Ramnd queue file is unreadable and will be replaced on the next save: {ex.Message}");
            return new List<Dictionary<string, object>>();
        }
    }

    public void Save(IReadOnlyList<Dictionary<string, object>> events)
    {
        try
        {
            var snapshot = new Snapshot
            {
                gateway = _gateway,
                events = new List<Dictionary<string, object>>(events ?? Array.Empty<Dictionary<string, object>>()),
            };
            string json = JsonConvert.SerializeObject(snapshot, JsonSettings);
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_path))
                File.Replace(tmp, _path, null);
            else
                File.Move(tmp, _path);
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"Failed to save Ramnd queue: {ex.Message}");
        }
    }

    sealed class Snapshot
    {
        public string gateway;
        public List<Dictionary<string, object>> events;
    }
}
