using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// <see cref="IAnalyticsSystem"/> that batches events and POSTs them to the RamnD game-services gateway.
/// Envelopes are frozen at <see cref="LogEvent{T}"/> and kept on disk until the gateway accepts them,
/// so an offline session is replayed on the next launch, <see cref="Flush"/>, or network return.
/// </summary>
public sealed class RamndAnalyticSystem : IAnalyticsSystem
{
    /// <summary>Oldest events past this cap are dropped. Matches the gateway batch limit.</summary>
    internal const int DiskQueueCap = 500;

    readonly RamndAnalyticsConfig _config;
    readonly IRamndAnalyticsTransport _transport;
    readonly IRamndAnalyticsQueueStore _store;
    readonly string _userId;
    readonly string _platform;
    readonly object _gate = new object();
    readonly List<Dictionary<string, object>> _queue = new List<Dictionary<string, object>>();
    readonly HashSet<string> _inflight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    string _playerId;
    Task _flushTask = Task.CompletedTask;
    bool _disposed;

    public RamndAnalyticSystem(
        RamndAnalyticsConfig config,
        IRamndAnalyticsTransport transport = null,
        string userId = null,
        string platform = null,
        string playerId = null)
        : this(config, transport, userId, platform, playerId, store: null)
    {
    }

    internal RamndAnalyticSystem(
        RamndAnalyticsConfig config,
        IRamndAnalyticsTransport transport,
        string userId,
        string platform,
        string playerId,
        IRamndAnalyticsQueueStore store)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _transport = transport ?? new UnityWebRequestRamndTransport(config);
        _store = store ?? RamndAnalyticsFileQueue.ForPersistentData(config.BaseUrl);
        _userId = string.IsNullOrEmpty(userId)
            ? RamndAnalyticsEnvelopeBuilder.GetOrCreateInstallUserId()
            : userId;
        _platform = string.IsNullOrEmpty(platform)
            ? RamndAnalyticsEnvelopeBuilder.ResolvePlatformLabel()
            : platform;
        _playerId = playerId;

        lock (_gate)
        {
            _queue.AddRange(_store.Load());
            int dropped = TrimUnlocked();
            if (dropped > 0)
            {
                PersistUnlocked();
                AppLog.Warn("Analytics", $"Ramnd queue full — dropping {dropped} oldest event(s).");
            }

            if (NetworkStatus.IsOnline)
                StartFlushUnlocked();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        Application.quitting += OnQuitting;
        NetworkStatus.IsOnlineChanged += OnNetworkOnline;
    }

    /// <summary>Updates authenticated player id after UGS sign-in (nullable clears).</summary>
    public void SetPlayerId(string playerId) => _playerId = playerId;

    /// <inheritdoc/>
    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        if (_disposed)
            return;

        try
        {
            var row = RamndAnalyticsEnvelopeBuilder.BuildEventObject(
                eventPayload,
                _userId,
                _platform,
                _playerId);
            lock (_gate)
            {
                _queue.Add(row);
                int dropped = TrimUnlocked();
                PersistUnlocked();
                if (dropped > 0)
                {
                    AppLog.Warn(
                        "Analytics",
                        $"Ramnd queue full — dropping {dropped} oldest event(s).");
                }

                if (_queue.Count >= _config.MaxBatchSize)
                    StartFlushUnlocked();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"Ramnd event '{eventPayload.EventName}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Flush()
    {
        if (_disposed)
            return;

        try
        {
            _ = FlushAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"Ramnd Flush failed: {ex.Message}");
        }
    }

    /// <summary>Awaitable flush for tests and orderly shutdown.</summary>
    public Task FlushAsync()
    {
        if (_disposed)
            return Task.CompletedTask;

        lock (_gate)
            StartFlushUnlocked();
        return _flushTask;
    }

    /// <summary>
    /// Stops the in-flight POST. Queued envelopes stay on disk and are retried next session.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        Application.quitting -= OnQuitting;
        NetworkStatus.IsOnlineChanged -= OnNetworkOnline;

        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already torn down
        }

        lock (_gate)
            PersistUnlocked();

        _lifetime.Dispose();
    }

    void OnNetworkOnline(bool isOnline)
    {
        if (!isOnline || _disposed)
            return;
        Flush();
    }

    void StartFlushUnlocked()
    {
        if (_disposed || _queue.Count == 0 || _inflight.Count > 0 || !_flushTask.IsCompleted)
            return;

        var batch = CopyFrontUnlocked();
        if (batch.Count == 0)
            return;

        MarkInflightUnlocked(batch);
        string json = SerializeBatch(batch);
        _flushTask = DrainLoopAsync(batch, json);
    }

    async Task DrainLoopAsync(List<Dictionary<string, object>> batch, string json)
    {
        // One failure ends this drain. The batch stays on disk; the next Flush / online
        // transition / launch tries again. Do not tight-loop a dead gateway.
        while (!_disposed && !_lifetime.IsCancellationRequested)
        {
            if (!await TryPostBatchAsync(batch, json))
            {
                lock (_gate)
                    _inflight.Clear();
                return;
            }

            lock (_gate)
            {
                _inflight.Clear();
                if (_disposed || _queue.Count == 0)
                    return;
                batch = CopyFrontUnlocked();
                if (batch.Count == 0)
                    return;
                MarkInflightUnlocked(batch);
                json = SerializeBatch(batch);
            }
        }

        lock (_gate)
            _inflight.Clear();
    }

    async Task<bool> TryPostBatchAsync(List<Dictionary<string, object>> batch, string json)
    {
        if (_disposed || _lifetime.IsCancellationRequested)
            return false;

        try
        {
            RamndIngestResult result = await _transport.PostEventsAsync(json, _lifetime.Token);
            if (result.StatusCode == 200 || result.StatusCode == 207)
            {
                Acknowledge(batch);
                AppLog.Info("Analytics", $"Ramnd ingest ok ({batch.Count} events, HTTP {result.StatusCode})");
                return true;
            }

            if (result.StatusCode == 400 && TryDropPermanentRejects(batch, result.Body))
                return true;

            AppLog.Warn(
                "Analytics",
                $"Ramnd ingest HTTP {result.StatusCode}: {TrimBody(result.Body)}");
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"Ramnd ingest failed: {ex.Message}");
            return false;
        }
    }

    void Acknowledge(List<Dictionary<string, object>> batch)
    {
        lock (_gate)
        {
            RemoveIdsUnlocked(IdsOf(batch));
            PersistUnlocked();
        }
    }

    /// <summary>
    /// Schema rejects are permanent. A 400 without a <c>rejected</c> list (unknown app, bad body)
    /// stays queued so a later server fix can still accept the envelopes.
    /// </summary>
    bool TryDropPermanentRejects(List<Dictionary<string, object>> batch, string body)
    {
        if (!TryReadRejected(body, out JArray rejected) || rejected.Count == 0)
            return false;

        var dropIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dropIndexes = new HashSet<int>();
        for (int i = 0; i < rejected.Count; i++)
        {
            if (!(rejected[i] is JObject item))
                continue;
            string eventId = item.Value<string>("eventId");
            if (!string.IsNullOrEmpty(eventId))
                dropIds.Add(eventId);
            int? index = item.Value<int?>("index");
            if (index.HasValue && index.Value >= 0 && index.Value < batch.Count)
                dropIndexes.Add(index.Value);
        }

        if (rejected.Count >= batch.Count)
        {
            foreach (string id in IdsOf(batch))
                dropIds.Add(id);
        }
        else
        {
            foreach (int index in dropIndexes)
            {
                string id = EventId(batch[index]);
                if (!string.IsNullOrEmpty(id))
                    dropIds.Add(id);
            }
        }

        if (dropIds.Count == 0)
            return false;

        lock (_gate)
        {
            RemoveIdsUnlocked(dropIds);
            PersistUnlocked();
        }

        AppLog.Warn("Analytics", $"Ramnd ingest dropped {dropIds.Count} rejected event(s) (HTTP 400).");
        return true;
    }

    List<Dictionary<string, object>> CopyFrontUnlocked()
    {
        int count = Math.Min(_queue.Count, _config.MaxBatchSize);
        var batch = new List<Dictionary<string, object>>(count);
        for (int i = 0; i < count; i++)
            batch.Add(_queue[i]);
        return batch;
    }

    void MarkInflightUnlocked(List<Dictionary<string, object>> batch)
    {
        _inflight.Clear();
        foreach (string id in IdsOf(batch))
        {
            if (!string.IsNullOrEmpty(id))
                _inflight.Add(id);
        }
    }

    int TrimUnlocked()
    {
        int dropped = 0;
        while (_queue.Count > DiskQueueCap)
        {
            int victim = 0;
            while (victim < _queue.Count && _inflight.Contains(EventId(_queue[victim])))
                victim++;
            if (victim >= _queue.Count)
                break;
            _queue.RemoveAt(victim);
            dropped++;
        }

        return dropped;
    }

    void RemoveIdsUnlocked(HashSet<string> ids)
    {
        if (ids == null || ids.Count == 0)
            return;
        _queue.RemoveAll(row => ids.Contains(EventId(row)));
    }

    void PersistUnlocked() => _store.Save(_queue);

    static string SerializeBatch(List<Dictionary<string, object>> batch) =>
        JsonConvert.SerializeObject(new Dictionary<string, object> { ["events"] = batch });

    static HashSet<string> IdsOf(List<Dictionary<string, object>> batch)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < batch.Count; i++)
        {
            string id = EventId(batch[i]);
            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
        }

        return ids;
    }

    static string EventId(Dictionary<string, object> row)
    {
        if (row != null && row.TryGetValue("event_id", out object id) && id != null)
            return id.ToString();
        return string.Empty;
    }

    static bool TryReadRejected(string body, out JArray rejected)
    {
        rejected = null;
        if (string.IsNullOrWhiteSpace(body))
            return false;
        try
        {
            if (!(JObject.Parse(body)["rejected"] is JArray array))
                return false;
            rejected = array;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

#if UNITY_EDITOR
    void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            Dispose();
    }
#endif

    void OnQuitting() => Dispose();

    static string TrimBody(string body)
    {
        if (string.IsNullOrEmpty(body))
            return string.Empty;
        return body.Length <= 200 ? body : body.Substring(0, 200) + "…";
    }
}
