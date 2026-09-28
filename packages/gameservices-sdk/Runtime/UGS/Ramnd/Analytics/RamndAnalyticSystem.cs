using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

/// <summary>
/// <see cref="IAnalyticsSystem"/> that batches events and POSTs them to the RamnD game-services gateway.
/// </summary>
public sealed class RamndAnalyticSystem : IAnalyticsSystem
{
    readonly RamndAnalyticsConfig _config;
    readonly IRamndAnalyticsTransport _transport;
    readonly string _userId;
    readonly string _platform;
    readonly object _gate = new object();
    readonly List<Dictionary<string, object>> _queue = new List<Dictionary<string, object>>();
    string _playerId;
    Task _flushTask = Task.CompletedTask;

    public RamndAnalyticSystem(
        RamndAnalyticsConfig config,
        IRamndAnalyticsTransport transport = null,
        string userId = null,
        string platform = null,
        string playerId = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _transport = transport ?? new UnityWebRequestRamndTransport(config);
        _userId = string.IsNullOrEmpty(userId)
            ? RamndAnalyticsEnvelopeBuilder.GetOrCreateInstallUserId()
            : userId;
        _platform = string.IsNullOrEmpty(platform)
            ? RamndAnalyticsEnvelopeBuilder.ResolvePlatformLabel()
            : platform;
        _playerId = playerId;
    }

    /// <summary>Updates authenticated player id after UGS sign-in (nullable clears).</summary>
    public void SetPlayerId(string playerId) => _playerId = playerId;

    /// <inheritdoc/>
    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
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
        lock (_gate)
            StartFlushUnlocked();
        return _flushTask;
    }

    void StartFlushUnlocked()
    {
        if (_queue.Count == 0)
            return;
        if (!_flushTask.IsCompleted)
            return;

        var batch = TakeBatchUnlocked();
        if (batch.Count == 0)
            return;

        string json = JsonConvert.SerializeObject(new Dictionary<string, object> { ["events"] = batch });
        _flushTask = DrainLoopAsync(batch, json);
    }

    async Task DrainLoopAsync(List<Dictionary<string, object>> batch, string json)
    {
        await PostBatchAsync(batch, json);

        while (true)
        {
            List<Dictionary<string, object>> nextBatch;
            string nextJson;
            lock (_gate)
            {
                if (_queue.Count == 0)
                    return;
                nextBatch = TakeBatchUnlocked();
                if (nextBatch.Count == 0)
                    return;
                nextJson = JsonConvert.SerializeObject(
                    new Dictionary<string, object> { ["events"] = nextBatch });
            }

            await PostBatchAsync(nextBatch, nextJson);
        }
    }

    async Task PostBatchAsync(List<Dictionary<string, object>> batch, string json)
    {
        try
        {
            RamndIngestResult result = await _transport.PostEventsAsync(json, CancellationToken.None);
            if (!result.IsSuccess)
            {
                AppLog.Warn(
                    "Analytics",
                    $"Ramnd ingest HTTP {result.StatusCode}: {TrimBody(result.Body)}");
                RequeueFront(batch);
            }
            else
            {
                AppLog.Info("Analytics", $"Ramnd ingest ok ({batch.Count} events, HTTP {result.StatusCode})");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"Ramnd ingest failed: {ex.Message}");
            RequeueFront(batch);
        }
    }

    List<Dictionary<string, object>> TakeBatchUnlocked()
    {
        int count = Math.Min(_queue.Count, _config.MaxBatchSize);
        var batch = new List<Dictionary<string, object>>(count);
        for (int i = 0; i < count; i++)
            batch.Add(_queue[i]);
        _queue.RemoveRange(0, count);
        return batch;
    }

    void RequeueFront(List<Dictionary<string, object>> batch)
    {
        lock (_gate)
        {
            _queue.InsertRange(0, batch);
            int cap = _config.MaxBatchSize * 2;
            if (_queue.Count > cap)
                _queue.RemoveRange(cap, _queue.Count - cap);
        }
    }

    static string TrimBody(string body)
    {
        if (string.IsNullOrEmpty(body))
            return string.Empty;
        return body.Length <= 200 ? body : body.Substring(0, 200) + "…";
    }
}
