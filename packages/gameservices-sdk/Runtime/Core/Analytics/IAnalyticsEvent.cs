/// <summary>
/// Marker interface for analytics events.
/// Implement on a struct with fields marked by <see cref="AnalyticsKeyAttribute"/>.
/// </summary>
/// <example>
/// <code>
/// public struct LevelStartEvent : IAnalyticsEvent
/// {
///     public string EventName => "level_start";
///     [AnalyticsKey("level_id")] public int LevelId;
/// }
/// // Usage:
/// GameServicesLocator.Services?.Analytics.LogEvent(new LevelStartEvent { LevelId = 3 });
/// </code>
/// </example>
public interface IAnalyticsEvent
{
    /// <summary>Event name in the analytics backend (snake_case recommended).</summary>
    string EventName { get; }
}
