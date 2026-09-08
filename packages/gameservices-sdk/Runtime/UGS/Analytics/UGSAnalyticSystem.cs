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
        _playerId = playerId;
        _sdk = sdk;
        if (IsEditorProductionCollectionDisabled)
        {
            AppLog.Warn("Analytics", "Non-player build + production: StartDataCollection skipped.");
            return;
        }

        // SDK v6+: enable data collection. Without this, RecordEvent is silently ignored.
        // TODO(analytics-consent): StartDataCollection is deprecated — migrate to EndUserConsent / store policy (see UGS Analytics 6+ docs).
#pragma warning disable CS0618
        sdk.StartDataCollection();
#pragma warning restore CS0618
    }

    /// <summary>
    /// True when UGS collection must not start at all. Covers Editor Play on production
    /// and desktop player builds on production — neither is a real player, and both showed up
    /// in the production dataset as <c>PC_CLIENT</c> / <c>LINUX_CLIENT</c> sessions.
    /// </summary>
    internal static bool IsEditorProductionCollectionDisabled =>
#if UGS_ENV_PRODUCTION && (UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL)
        true;
#else
        false;
#endif

    internal string PlayerId => _playerId;

    /// <inheritdoc/>
    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        try
        {
            RecordEventCore(eventPayload);
        }
        catch (System.Exception ex)
        {
            AppLog.Error("Analytics", $"Event '{eventPayload.EventName}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Records without swallowing exceptions — used by <see cref="CachedAnalyticsSystem"/>
    /// so failed sends fall through to the offline queue.
    /// </summary>
    internal void LogEventOrThrow<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        RecordEventCore(eventPayload);
    }

    void RecordEventCore<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        if (IsEditorProductionCollectionDisabled || _sdk == null)
            return;

        var customEvent = eventPayload.ToCustomEvent();
        AnalyticsCustomEventEnricher.ApplyUgsPlayerId(customEvent, _playerId);
        _sdk.RecordEvent(customEvent);
        AppLog.Info("Analytics", $"{eventPayload.EventName}");
    }

    /// <inheritdoc/>
    public void Flush()
    {
        if (IsEditorProductionCollectionDisabled || _sdk == null)
            return;

        try { _sdk.Flush(); }
        catch (System.Exception ex) { AppLog.Error("Analytics", $"Flush error: {ex.Message}"); }
    }
}
