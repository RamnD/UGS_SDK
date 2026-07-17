using Unity.Services.Analytics;
using UnityEngine;

/// <summary>
/// <see cref="IAnalyticsSystem"/> implementation via Unity Gaming Services Analytics SDK.
/// <para>
/// To swap backends — implement <see cref="IAnalyticsSystem"/> in another class
/// and pass the new instance to <see cref="UGSServicesBuilder"/>.
/// </para>
/// Usage:
/// <code>
/// GameServicesLocator.Services?.Analytics.LogEvent(new LevelStartEvent { LevelId = 3 });
/// </code>
/// </summary>
public class UGSAnalyticSystem : IAnalyticsSystem
{
    private readonly string _playerId;
    private readonly IAnalyticsService _sdk;

    /// <param name="playerId">UGS Authentication player id — injected into every custom event.</param>
    /// <param name="sdk">SDK injected from outside for testability. Pass Unity.Services.Analytics.AnalyticsService.Instance.</param>
    public UGSAnalyticSystem(string playerId, IAnalyticsService sdk)
    {
        // SDK v6+: enable data collection. Without this, RecordEvent is silently ignored.
        // TODO(analytics-consent): StartDataCollection is deprecated — migrate to EndUserConsent / store policy (see UGS Analytics 6+ docs).
#pragma warning disable CS0618
        sdk.StartDataCollection();
#pragma warning restore CS0618
        _playerId = playerId;
        _sdk = sdk;
    }

    internal string PlayerId => _playerId;

    /// <inheritdoc/>
    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        try
        {
            var customEvent = eventPayload.ToCustomEvent();
            AnalyticsCustomEventEnricher.ApplyUgsPlayerId(customEvent, _playerId);
            _sdk.RecordEvent(customEvent);
            Debug.Log($"[Analytics] {eventPayload.EventName}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Analytics] Event '{eventPayload.EventName}' failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Flush()
    {
        try { _sdk.Flush(); }
        catch (System.Exception ex) { Debug.LogError($"[Analytics] Flush error: {ex.Message}"); }
    }
}
