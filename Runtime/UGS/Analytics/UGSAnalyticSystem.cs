using Unity.Services.Analytics;
using UnityEngine;

/// <summary>
/// Реализация <see cref="IAnalyticsSystem"/> через Unity Gaming Services Analytics SDK.
/// <para>
/// Для замены бэкенда — реализуйте <see cref="IAnalyticsSystem"/> в другом классе
/// и передайте новый экземпляр в <see cref="UGSServicesBuilder"/>.
/// </para>
/// Использование:
/// <code>
/// GameServicesLocator.Services?.Analytics.LogEvent(new LevelStartEvent { LevelId = 3 });
/// </code>
/// </summary>
public class UGSAnalyticSystem : IAnalyticsSystem
{
    private readonly IAnalyticsService _sdk;

    /// <param name="playerId">UGS Player ID — зарезервирован для будущего использования (custom user ID).</param>
    /// <param name="sdk">SDK инжектируется снаружи для тестируемости. Передавайте Unity.Services.Analytics.AnalyticsService.Instance.</param>
    public UGSAnalyticSystem(string playerId, IAnalyticsService sdk)
    {
        // SDK v6+: включаем сбор данных. Без этого RecordEvent молча игнорируется.
        // TODO(analytics-consent): StartDataCollection устаревает — мигрировать на EndUserConsent / политику магазина (см. документацию UGS Analytics 6+).
#pragma warning disable CS0618
        sdk.StartDataCollection();
#pragma warning restore CS0618
        _sdk = sdk;
    }

    /// <inheritdoc/>
    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        try
        {
            var customEvent = eventPayload.ToCustomEvent();
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
