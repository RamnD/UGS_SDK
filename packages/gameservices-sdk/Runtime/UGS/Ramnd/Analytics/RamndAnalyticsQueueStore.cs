using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
/// Disk queue of frozen ingest envelopes. The snapshot file is stamped with the gateway
/// base URL so a staging queue is not replayed against production.
/// New envelopes are appended to a journal so a gameplay <c>LogEvent</c> does not rewrite
/// the whole queue on the calling thread. Removals compact a new snapshot generation; a
/// journal left behind by a crash is ignored once that generation advances.
/// </summary>
internal sealed class RamndAnalyticsFileQueue : IRamndAnalyticsQueueStore
{
    internal const string FileName = "ramnd-analytics-queue.json";
    internal const string JournalFileName = "ramnd-analytics-queue.journal.jsonl";

    static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        DateParseHandling = DateParseHandling.None,
        NullValueHandling = NullValueHandling.Ignore,
    };

    readonly string _path;
    readonly string _journalPath;
    readonly string _gateway;
    readonly List<string> _checkpointIds = new List<string>();
    int _generation;
    bool _checkpointReady;
    bool _canAppend;
    bool _snapshotUnreadable;

    public RamndAnalyticsFileQueue(string directory, string gateway)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory is required.", nameof(directory));
        if (string.IsNullOrWhiteSpace(gateway))
            throw new ArgumentException("Gateway is required.", nameof(gateway));

        _path = Path.Combine(directory, FileName);
        _journalPath = Path.Combine(directory, JournalFileName);
        _gateway = gateway.TrimEnd('/');
    }

    public static RamndAnalyticsFileQueue ForPersistentData(string gateway) =>
        new RamndAnalyticsFileQueue(Application.persistentDataPath, gateway);

    public List<Dictionary<string, object>> Load()
    {
        try
        {
            var events = ReadCombined();
            Remember(events);
            return events;
        }
        catch (Exception ex)
        {
            // An empty checkpoint appends later events instead of replacing a file this
            // process could not read. A parse failure is handled in ReadSnapshot and still repairs.
            Remember(new List<Dictionary<string, object>>());
            _generation = 0;
            _snapshotUnreadable = false;
            AppLog.Error(
                "Analytics",
                $"Ramnd queue file could not be read; the previous checkpoint was left in place: {ex.Message}");
            return new List<Dictionary<string, object>>();
        }
    }

    public void Save(IReadOnlyList<Dictionary<string, object>> events)
    {
        var copy = new List<Dictionary<string, object>>(events?.Count ?? 0);
        if (events != null)
        {
            for (int i = 0; i < events.Count; i++)
                copy.Add(events[i]);
        }
        try
        {
            EnsureCheckpoint();
            if (_snapshotUnreadable || !_canAppend || !TryAppendSuffix(copy))
                RewriteSnapshot(copy);
        }
        catch (Exception ex)
        {
            _checkpointReady = false;
            AppLog.Error("Analytics", $"Failed to save Ramnd queue: {ex.Message}");
        }
    }

    void EnsureCheckpoint()
    {
        if (_checkpointReady)
            return;

        var events = ReadCombined();
        Remember(events);
    }

    List<Dictionary<string, object>> ReadCombined()
    {
        var events = ReadSnapshot();
        ReadJournalInto(events);
        return events;
    }

    List<Dictionary<string, object>> ReadSnapshot()
    {
        _snapshotUnreadable = false;
        if (!File.Exists(_path))
        {
            _generation = 0;
            return new List<Dictionary<string, object>>();
        }

        try
        {
            var snapshot = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(_path), JsonSettings);
            if (snapshot == null)
            {
                _generation = 0;
                return new List<Dictionary<string, object>>();
            }

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

                // Do not inherit another gateway's generation or delete its snapshot.
                _generation = 0;
                return new List<Dictionary<string, object>>();
            }

            _generation = snapshot.generation < 0 ? 0 : snapshot.generation;
            return snapshot.events ?? new List<Dictionary<string, object>>();
        }
        catch (Exception ex)
        {
            _snapshotUnreadable = true;
            _generation = 0;
            AppLog.Error(
                "Analytics",
                $"Ramnd queue file is unreadable and will be replaced on the next save: {ex.Message}");
            return new List<Dictionary<string, object>>();
        }
    }

    void ReadJournalInto(List<Dictionary<string, object>> events)
    {
        if (!File.Exists(_journalPath))
            return;

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < events.Count; i++)
        {
            string id = EventId(events[i]);
            if (id != null)
                known.Add(id);
        }

        var parsedLines = new List<JournalLine>();
        string rawJournal = File.ReadAllText(_journalPath);
        string[] lines = rawJournal.Split('\n');

        bool journalIsTorn = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0)
                continue;

            JournalLine parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<JournalLine>(line, JsonSettings);
            }
            catch (JsonException)
            {
                journalIsTorn = true;
                continue;
            }

            if (parsed?.Event == null)
                continue;
            if (!string.Equals(parsed.gateway, _gateway, StringComparison.Ordinal))
                continue;
            if (parsed.generation < 0)
                parsed.generation = 0;
            parsedLines.Add(parsed);
        }

        // A corrupt snapshot has no generation. Keep only the newest journal generation so a
        // stale prefix left by a crash cannot resurrect events the snapshot already dropped.
        int generation = _generation;
        if (_snapshotUnreadable)
        {
            int? newest = null;
            for (int i = 0; i < parsedLines.Count; i++)
            {
                if (!newest.HasValue || parsedLines[i].generation > newest.Value)
                    newest = parsedLines[i].generation;
            }

            if (!newest.HasValue)
                return;
            generation = newest.Value;
            _generation = generation;
        }

        for (int i = 0; i < parsedLines.Count; i++)
        {
            JournalLine parsed = parsedLines[i];
            if (parsed.generation != generation)
                continue;

            string id = EventId(parsed.Event);
            if (id != null && !known.Add(id))
                continue;
            events.Add(parsed.Event);
        }

        if (journalIsTorn)
        {
            try
            {
                // Compact torn tails back to valid JSON lines immediately
                // to prevent the next save's append from merging onto a bad fragment.
                if (parsedLines.Count == 0)
                {
                    File.Delete(_journalPath);
                }
                else
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < parsedLines.Count; i++)
                    {
                        sb.Append(JsonConvert.SerializeObject(parsedLines[i], JsonSettings));
                        sb.Append('\n');
                    }
                    File.WriteAllText(_journalPath, sb.ToString());
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Analytics", $"Failed to repair torn journal: {ex.Message}");
                // Mark for snapshot rewrite on next save instead of appending to corrupt file.
                _canAppend = false;
            }
        }
    }

    bool TryAppendSuffix(List<Dictionary<string, object>> events)
    {
        var incomingIds = new List<string>(events.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < events.Count; i++)
        {
            string id = EventId(events[i]);
            if (id == null || !seen.Add(id))
                return false;
            incomingIds.Add(id);
        }

        if (incomingIds.Count < _checkpointIds.Count)
            return false;

        for (int i = 0; i < _checkpointIds.Count; i++)
        {
            if (!string.Equals(incomingIds[i], _checkpointIds[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (incomingIds.Count == _checkpointIds.Count)
            return true;

        AppendJournal(events, _checkpointIds.Count);
        for (int i = _checkpointIds.Count; i < incomingIds.Count; i++)
            _checkpointIds.Add(incomingIds[i]);
        return true;
    }

    void AppendJournal(List<Dictionary<string, object>> events, int start)
    {
        EnsureDirectory();
        var sb = new StringBuilder();
        for (int i = start; i < events.Count; i++)
        {
            var line = new JournalLine
            {
                gateway = _gateway,
                generation = _generation,
                Event = events[i],
            };
            sb.Append(JsonConvert.SerializeObject(line, JsonSettings));
            sb.Append('\n');
        }

        File.AppendAllText(_journalPath, sb.ToString());
    }

    void RewriteSnapshot(List<Dictionary<string, object>> events)
    {
        EnsureDirectory();
        int nextGeneration = _generation + 1;
        if (nextGeneration < 0)
            nextGeneration = 1;

        var snapshot = new Snapshot
        {
            gateway = _gateway,
            generation = nextGeneration,
            events = events,
        };
        string json = JsonConvert.SerializeObject(snapshot, JsonSettings);
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(_path))
            File.Replace(tmp, _path, null);
        else
            File.Move(tmp, _path);

        // Snapshot generation already advanced, so a crash before this delete cannot
        // replay the old journal on top of the compacted queue.
        if (File.Exists(_journalPath))
            File.Delete(_journalPath);

        _generation = nextGeneration;
        _snapshotUnreadable = false;
        Remember(events);
    }

    void Remember(List<Dictionary<string, object>> events)
    {
        _checkpointIds.Clear();
        _canAppend = true;
        _checkpointReady = true;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < events.Count; i++)
        {
            string id = EventId(events[i]);
            if (id == null || !seen.Add(id))
            {
                _canAppend = false;
                _checkpointIds.Clear();
                return;
            }

            _checkpointIds.Add(id);
        }
    }

    void EnsureDirectory()
    {
        string directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    static string EventId(Dictionary<string, object> row)
    {
        if (row != null && row.TryGetValue("event_id", out object id) && id != null)
        {
            string text = id.ToString();
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        return null;
    }

    sealed class Snapshot
    {
        public string gateway;
        public int generation;
        public List<Dictionary<string, object>> events;
    }

    sealed class JournalLine
    {
        public string gateway;
        public int generation;

        [JsonProperty("event")]
        public Dictionary<string, object> Event;
    }
}
