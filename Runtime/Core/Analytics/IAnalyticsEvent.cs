/// <summary>
/// Маркерный интерфейс для событий аналитики.
/// Реализуйте в struct с полями, помеченными <see cref="AnalyticsKeyAttribute"/>.
/// </summary>
/// <example>
/// <code>
/// public struct LevelStartEvent : IAnalyticsEvent
/// {
///     public string EventName => "level_start";
///     [AnalyticsKey("level_id")] public int LevelId;
/// }
/// // Использование:
/// GameServicesLocator.Services?.Analytics.LogEvent(new LevelStartEvent { LevelId = 3 });
/// </code>
/// </example>
public interface IAnalyticsEvent
{
    /// <summary>Имя события в аналитическом бэкенде (рекомендуется snake_case).</summary>
    string EventName { get; }
}
