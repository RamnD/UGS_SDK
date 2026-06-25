using System.Collections.Generic;
using System.Reflection;
using Unity.Services.Analytics;

/// <summary>
/// Вспомогательные методы для конвертации struct-событий в формат UGS Analytics SDK.
/// Используются внутри <see cref="UGSAnalyticSystem"/> — не вызывайте напрямую.
/// </summary>
public static class AnalyticsExtensions
{
    /// <summary>
    /// Конвертирует struct, реализующий <see cref="IAnalyticsEvent"/>, в <see cref="CustomEvent"/> для UGS SDK.
    /// Включаются только поля с атрибутом <see cref="AnalyticsKeyAttribute"/>.
    /// </summary>
    public static CustomEvent ToCustomEvent<T>(this T dataObject) where T : struct, IAnalyticsEvent
    {
        var customEvent = new CustomEvent(dataObject.EventName);
        var dict = dataObject.ToAnalyticsDict();
        foreach (var kv in dict)
        {
            switch (kv.Value)
            {
                case bool   b: customEvent.Add(kv.Key, b); break;
                case int    i: customEvent.Add(kv.Key, i); break;
                case float  f: customEvent.Add(kv.Key, f); break;
                case long   l: customEvent.Add(kv.Key, l); break;
                case double d: customEvent.Add(kv.Key, d); break;
                case string s: customEvent.Add(kv.Key, s); break;
                default: customEvent.Add(kv.Key, kv.Value?.ToString() ?? ""); break;
            }
        }
        return customEvent;
    }

    /// <summary>
    /// Собирает все публичные поля с <see cref="AnalyticsKeyAttribute"/> в словарь key → value.
    /// </summary>
    private static Dictionary<string, object> ToAnalyticsDict<T>(this T dataObject) where T : struct
    {
        var dict   = new Dictionary<string, object>();
        var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (var field in fields)
        {
            var attr = field.GetCustomAttribute<AnalyticsKeyAttribute>();
            if (attr == null) continue;

            object value = field.GetValue(dataObject);
            if (value == null) continue;

            if (field.FieldType.IsEnum)
                value = value.ToString();

            dict.Add(attr.Key, value);
        }

        return dict;
    }
}
