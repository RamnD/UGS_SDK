using System;

/// <summary>
/// Отмечает поле или свойство struct-события как параметр аналитики.
/// Строка <see cref="Key"/> используется как имя параметра в SDK (snake_case).
/// Поля без этого атрибута игнорируются при сериализации.
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
    /// <summary>Имя параметра в аналитическом SDK.</summary>
    public string Key { get; }

    public AnalyticsKeyAttribute(string key) => Key = key;
}
