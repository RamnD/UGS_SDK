using Unity.Services.Analytics;
using UnityEngine;

/// <summary>
/// Decorator that persists analytics events while offline and replays them on reconnect / flush.
/// Supports a pre-auth mode: events queue until <see cref="AttachInner"/> is called after sign-in.
/// </summary>
public sealed class CachedAnalyticsSystem : IAnalyticsSystem
{
    IAnalyticsService _sdk;
    UGSAnalyticSystem _inner;
    string _playerId;
    readonly PendingAnalyticsQueue _queue = new PendingAnalyticsQueue();
    bool _subscribedToNetwork;

    /// <summary>Queue-only instance for use before UGS auth completes.</summary>
    public static CachedAnalyticsSystem CreatePreAuth()
    {
        var instance = new CachedAnalyticsSystem();
        instance.SubscribeToNetworkStatus();
        return instance;
    }

    CachedAnalyticsSystem() { }

    public CachedAnalyticsSystem(UGSAnalyticSystem inner, IAnalyticsService sdk)
    {
        AttachInner(inner, sdk);
    }

    /// <summary>Connects the live UGS analytics backend and replays any queued events.</summary>
    public void AttachInner(UGSAnalyticSystem inner, IAnalyticsService sdk)
    {
        _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
        _sdk = sdk ?? throw new System.ArgumentNullException(nameof(sdk));
        _playerId = inner.PlayerId;
        SubscribeToNetworkStatus();
        DrainQueue();
    }

    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        if (CanSendImmediately())
        {
            if (TrySendImmediate(eventPayload))
                return;
        }

        _queue.Enqueue(AnalyticsEventSerializer.ToRecord(eventPayload));
    }

    public void Flush()
    {
        DrainQueue();
        _inner?.Flush();
    }

    bool CanSendImmediately() =>
        NetworkStatus.IsOnline && _inner != null && _sdk != null;

    bool TrySendImmediate<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        try
        {
            _inner.LogEventOrThrow(eventPayload);
            DrainQueue();
            return true;
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("Analytics", $"Immediate send failed, queueing event '{eventPayload.EventName}': {ex.Message}");
            return false;
        }
    }

    void SubscribeToNetworkStatus()
    {
        if (_subscribedToNetwork)
            return;
        _subscribedToNetwork = true;
        NetworkStatus.IsOnlineChanged += OnNetworkStatusChanged;
    }

    void OnNetworkStatusChanged(bool isOnline)
    {
        if (isOnline)
            DrainQueue();
    }

    void DrainQueue()
    {
        if (_inner == null || _sdk == null || !NetworkStatus.IsOnline)
            return;

        // Refresh playerId from inner at batch start in case it changed after re-auth.
        if (_inner != null)
            _playerId = _inner.PlayerId;

        const int batchSize = 64;
        while (true)
        {
            var batch = _queue.DequeueBatch(batchSize);
            if (batch.Count == 0)
                break;

            for (int i = 0; i < batch.Count; i++)
            {
                PendingAnalyticsRecord record = batch[i];
                try
                {
                    CustomEvent customEvent = AnalyticsEventSerializer.ToCustomEvent(record);
                    AnalyticsCustomEventEnricher.ApplyUgsPlayerId(customEvent, _playerId ?? _inner?.PlayerId);
                    _sdk.RecordEvent(customEvent);
                    AppLog.Info("Analytics", $"Replayed queued event '{record.eventName}'");
                }
                catch (System.Exception ex)
                {
                    AppLog.Error("Analytics", $"Failed to replay queued event '{record?.eventName}': {ex.Message}");
                    var remaining = new System.Collections.Generic.List<PendingAnalyticsRecord>();
                    for (int j = i; j < batch.Count; j++)
                        remaining.Add(batch[j]);
                    _queue.RequeueFront(remaining);
                    return;
                }
            }
        }
    }
}
