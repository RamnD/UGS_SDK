using System.Collections.Generic;
using System.Reflection;
using Unity.Services.Analytics;

/// <summary>
/// Helpers for converting event structs to UGS Analytics SDK format.
/// Used inside <see cref="UGSAnalyticSystem"/> — do not call directly.
/// </summary>
public static class AnalyticsExtensions
{
    /// <summary>
    /// Converts a struct implementing <see cref="IAnalyticsEvent"/> to a <see cref="CustomEvent"/> for the UGS SDK.
    /// Only fields with <see cref="AnalyticsKeyAttribute"/> are included.
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
    /// Collects all public fields with <see cref="AnalyticsKeyAttribute"/> into a key → value dictionary.
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
