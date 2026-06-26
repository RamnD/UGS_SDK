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
    readonly PendingAnalyticsQueue _queue = new PendingAnalyticsQueue();

    /// <summary>Queue-only instance for use before UGS auth completes.</summary>
    public static CachedAnalyticsSystem CreatePreAuth() => new CachedAnalyticsSystem();

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
            _inner.LogEvent(eventPayload);
            DrainQueue();
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Analytics] Immediate send failed, queueing event '{eventPayload.EventName}': {ex.Message}");
            return false;
        }
    }

    void DrainQueue()
    {
        if (_inner == null || _sdk == null || !NetworkStatus.IsOnline)
            return;

        while (_queue.TryDequeue(out PendingAnalyticsRecord record))
        {
            try
            {
                CustomEvent customEvent = AnalyticsEventSerializer.ToCustomEvent(record);
                _sdk.RecordEvent(customEvent);
                Debug.Log($"[Analytics] Replayed queued event '{record.eventName}'");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Analytics] Failed to replay queued event '{record?.eventName}': {ex.Message}");
                if (record != null)
                    _queue.Enqueue(record);
                return;
            }
        }
    }
}
