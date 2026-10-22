using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// <see cref="IAnalyticsSystem"/> that batches events and POSTs them to the RamnD game-services gateway.
/// Envelopes are frozen at <see cref="LogEvent{T}"/> and checkpointed to disk before that call
/// returns, then kept until the gateway accepts them, including after a force-kill.
/// While online, a non-empty queue is sent on <see cref="RamndAnalyticsConfig.FlushInterval"/>,
/// on <see cref="Flush"/>, when the network returns, and again after a failed attempt (backoff).
/// A full batch is an automatic flush: it waits for that backoff and for the device to be online.
/// </summary>
public sealed class RamndAnalyticSystem : IAnalyticsSystem
{
    /// <summary>Oldest events past this cap are dropped. Matches the gateway batch limit.</summary>
    internal const int DiskQueueCap = 500;

    internal const double RetryBaseSeconds = 2;
    internal const double RetryCapSeconds = 60;

    readonly RamndAnalyticsConfig _config;
    readonly IRamndAnalyticsTransport _transport;
    readonly IRamndAnalyticsQueueStore _store;
    readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    readonly Func<bool> _isOnline;
    readonly System.Random _random = new System.Random();
    readonly string _userId;
    readonly string _platform;
    readonly object _gate = new object();
    readonly List<Dictionary<string, object>> _queue = new List<Dictionary<string, object>>();
    readonly HashSet<string> _inflight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
    string _playerId;
    Task _flushTask = Task.CompletedTask;
    int _consecutiveFailures;
    int _retryGeneration;
    DateTime _nextRetryUtc = DateTime.MinValue;
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
        IRamndAnalyticsQueueStore store,
        Func<TimeSpan, CancellationToken, Task> delayAsync = null,
        Func<bool> isOnline = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _transport = transport ?? new UnityWebRequestRamndTransport(config);
        _store = store ?? RamndAnalyticsFileQueue.ForPersistentData(config.BaseUrl);
        _delayAsync = delayAsync ?? DelayAsync;
        _isOnline = isOnline ?? (() => NetworkStatus.IsOnline);
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

            if (_isOnline())
                StartFlushUnlocked();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        Application.quitting += OnQuitting;
        NetworkStatus.IsOnlineChanged += OnNetworkOnline;
        _ = PeriodicFlushLoop();
    }

    /// <summary>
    /// Delay before the next automatic retry after <paramref name="failureCount"/> failed attempts.
    /// <paramref name="jitterUnit"/> is 0..1 and spreads the delay by ±25%.
    /// </summary>
    internal static TimeSpan ComputeRetryDelay(int failureCount, double jitterUnit)
    {
        if (failureCount < 1)
            failureCount = 1;
        int shift = Math.Min(failureCount - 1, 5);
        double baseSeconds = Math.Min(RetryCapSeconds, RetryBaseSeconds * Math.Pow(2, shift));
        double unit = Math.Min(1d, Math.Max(0d, jitterUnit));
        double jitter = 0.75d + (unit * 0.5d);
        return TimeSpan.FromMilliseconds(baseSeconds * jitter * 1000d);
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
                // Durable before return. A force-kill does not run Dispose, and a failed POST
                // does not drop the row. A suffix checkpoint is an append, not a full rewrite.
                PersistUnlocked();
                if (dropped > 0)
                {
                    AppLog.Warn(
                        "Analytics",
                        $"Ramnd queue full — dropping {dropped} oldest event(s).");
                }

                // Automatic, same as the timer. Explicit Flush / pause / quit / network return
                // clear the deadline and call FlushAsync, so they still send immediately.
                if (_queue.Count >= _config.MaxBatchSize && IsAutomaticFlushDueUnlocked())
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

        // Pause / quit / network return try immediately and cancel a pending backoff.
        lock (_gate)
        {
            _nextRetryUtc = DateTime.MinValue;
            _retryGeneration++;
        }

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

    /// <summary>
    /// True when an automatic trigger (timer or a full batch) may start a drain.
    /// Caller holds <see cref="_gate"/>. Does not apply to explicit <see cref="Flush"/>.
    /// </summary>
    bool IsAutomaticFlushDueUnlocked() =>
        !_disposed && _isOnline() && DateTime.UtcNow >= _nextRetryUtc;

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
        // One failure ends this drain. The batch stays on disk and a later backoff tick,
        // Flush, or the online transition tries again. Do not tight-loop a dead gateway.
        while (!_disposed && !_lifetime.IsCancellationRequested)
        {
            if (!await TryPostBatchAsync(batch, json))
            {
                // The batch stays queued. Checkpoint again in case the LogEvent write failed;
                // an unchanged queue does not rewrite the snapshot.
                lock (_gate)
                {
                    _inflight.Clear();
                    PersistUnlocked();
                }
                ScheduleRetry();
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
            if (result.StatusCode == 200)
            {
                Acknowledge(batch);
                NoteSuccess();
                AppLog.Info("Analytics", $"Ramnd ingest ok ({batch.Count} events, HTTP {result.StatusCode})");
                return true;
            }

            if (result.StatusCode == 207)
                return TryApplyPartialAccept(batch, result.Body);

            if (result.StatusCode == 400 && TryDropPermanentRejects(batch, result.Body))
            {
                NoteSuccess();
                return true;
            }

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

    /// <summary>
    /// 207 stores some events and permanently rejects others. Drop <c>rejected</c> ids,
    /// confirm the rest, and keep the whole batch when the list cannot be applied.
    /// A replay is safe: the gateway dedupes <c>event_id</c>.
    /// </summary>
    bool TryApplyPartialAccept(List<Dictionary<string, object>> batch, string body)
    {
        if (!TryReadRejected(body, out JArray rejected))
        {
            AppLog.Warn(
                "Analytics",
                "Ramnd ingest HTTP 207 without a rejected list; keeping the batch for retry.");
            return false;
        }

        if (!TryResolvePartial(batch, rejected, out HashSet<string> dropIds))
        {
            AppLog.Warn(
                "Analytics",
                "Ramnd ingest HTTP 207 rejected list could not be applied; keeping the batch for retry.");
            return false;
        }

        int dropCount = 0;
        foreach (string id in IdsOf(batch))
        {
            if (dropIds.Contains(id))
                dropCount++;
        }

        Acknowledge(batch);

        if (dropCount > 0)
        {
            AppLog.Warn(
                "Analytics",
                $"Ramnd ingest dropped {dropCount} rejected event(s) (HTTP 207).");
        }

        AppLog.Info(
            "Analytics",
            $"Ramnd ingest partial ({batch.Count - dropCount} confirmed, {dropCount} rejected, HTTP 207)");
        NoteSuccess();
        return true;
    }

    static bool TryResolvePartial(
        List<Dictionary<string, object>> batch,
        JArray rejected,
        out HashSet<string> dropIds)
    {
        dropIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rejected.Count == 0)
            return true;

        if (rejected.Count >= batch.Count)
        {
            dropIds.UnionWith(IdsOf(batch));
            return dropIds.Count > 0 || batch.Count == 0;
        }

        int unresolved = 0;
        for (int i = 0; i < rejected.Count; i++)
        {
            if (!(rejected[i] is JObject item))
            {
                unresolved++;
                continue;
            }

            string eventId = item.Value<string>("eventId");
            if (!string.IsNullOrEmpty(eventId))
            {
                dropIds.Add(eventId);
                continue;
            }

            int? index = item.Value<int?>("index");
            if (index.HasValue && index.Value >= 0 && index.Value < batch.Count)
            {
                string id = EventId(batch[index.Value]);
                if (!string.IsNullOrEmpty(id))
                {
                    dropIds.Add(id);
                    continue;
                }
            }

            unresolved++;
        }

        return unresolved == 0;
    }

    void NoteSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _nextRetryUtc = DateTime.MinValue;
        }
    }

    void ScheduleRetry()
    {
        if (_disposed || !_isOnline())
            return;

        try
        {
            if (_lifetime.IsCancellationRequested)
                return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        TimeSpan delay;
        int generation;
        lock (_gate)
        {
            if (_disposed)
                return;
            _consecutiveFailures++;
            delay = ComputeRetryDelay(_consecutiveFailures, _random.NextDouble());
            _nextRetryUtc = DateTime.UtcNow + delay;
            generation = ++_retryGeneration;
        }

        _ = RetryAfterAsync(delay, generation);
    }

    async Task RetryAfterAsync(TimeSpan delay, int generation)
    {
        try
        {
            await _delayAsync(delay, _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (_disposed)
            return;

        lock (_gate)
        {
            if (generation != _retryGeneration)
                return;
        }

        StartPeriodicFlush();
    }

    async Task PeriodicFlushLoop()
    {
        TimeSpan interval = _config.FlushInterval;
        if (interval <= TimeSpan.Zero)
            return;

        while (!_disposed)
        {
            try
            {
                await _delayAsync(interval, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (_disposed)
                continue;

            bool due;
            lock (_gate)
                due = IsAutomaticFlushDueUnlocked();
            if (due)
                StartPeriodicFlush();
        }
    }

    void StartPeriodicFlush()
    {
        if (_disposed)
            return;

        try
        {
            _ = FlushAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"Ramnd periodic flush failed: {ex.Message}");
        }
    }

    static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);

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

    // Caller holds _gate. File-store saves append a suffix and rewrite the snapshot only
    // when events are removed, so LogEvent does not hitch on a full-queue serialize.
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
