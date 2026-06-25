using System;

/// <summary>
/// Marks a field or property on an event struct as an analytics parameter.
/// The <see cref="Key"/> string is used as the parameter name in the SDK (snake_case).
/// Fields without this attribute are ignored during serialization.
/// </summary>
/// <example>
/// <code>
/// public struct LevelEndEvent : IAnalyticsEvent
/// {
///     public string EventName => "level_end";
///     [AnalyticsKey("level_id")]   public int LevelId;
///     [AnalyticsKey("is_win")]     public bool IsWin;
///     [AnalyticsKey("score")]      public int Score;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class AnalyticsKeyAttribute : Attribute
{
    /// <summary>Parameter name in the analytics SDK.</summary>
    public string Key { get; }

    public AnalyticsKeyAttribute(string key) => Key = key;
}
