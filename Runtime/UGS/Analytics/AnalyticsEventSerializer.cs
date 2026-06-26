using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Services.Analytics;

internal static class AnalyticsEventSerializer
{
    public static PendingAnalyticsRecord ToRecord<T>(T dataObject) where T : struct, IAnalyticsEvent
    {
        var record = new PendingAnalyticsRecord
        {
            eventName = dataObject.EventName,
        };

        var dict = ToAnalyticsDict(dataObject);
        if (dict.Count == 0)
            return record;

        var parameters = new PendingAnalyticsParam[dict.Count];
        int index = 0;
        foreach (var kv in dict)
        {
            parameters[index++] = new PendingAnalyticsParam
            {
                key = kv.Key,
                valueType = kv.Value?.GetType().Name ?? "string",
                value = ConvertToString(kv.Value),
            };
        }

        record.parameters = parameters;
        return record;
    }

    public static CustomEvent ToCustomEvent(PendingAnalyticsRecord record)
    {
        var customEvent = new CustomEvent(record.eventName ?? string.Empty);
        if (record.parameters == null)
            return customEvent;

        for (int i = 0; i < record.parameters.Length; i++)
        {
            PendingAnalyticsParam param = record.parameters[i];
            if (param == null || string.IsNullOrEmpty(param.key))
                continue;

            AddParameter(customEvent, param.key, param.valueType, param.value);
        }

        return customEvent;
    }

    static Dictionary<string, object> ToAnalyticsDict<T>(T dataObject) where T : struct
    {
        var dict = new Dictionary<string, object>();
        FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (FieldInfo field in fields)
        {
            var attr = field.GetCustomAttribute<AnalyticsKeyAttribute>();
            if (attr == null)
                continue;

            object value = field.GetValue(dataObject);
            if (value == null)
                continue;

            if (field.FieldType.IsEnum)
                value = value.ToString();

            dict.Add(attr.Key, value);
        }

        return dict;
    }

    static string ConvertToString(object value)
    {
        return value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            _ => value.ToString(),
        };
    }

    static void AddParameter(CustomEvent customEvent, string key, string valueType, string value)
    {
        switch (valueType)
        {
            case nameof(Boolean):
                if (bool.TryParse(value, out bool boolValue))
                    customEvent.Add(key, boolValue);
                break;
            case nameof(Int32):
                if (int.TryParse(value, out int intValue))
                    customEvent.Add(key, intValue);
                break;
            case nameof(Int64):
                if (long.TryParse(value, out long longValue))
                    customEvent.Add(key, longValue);
                break;
            case nameof(Single):
                if (float.TryParse(value, out float floatValue))
                    customEvent.Add(key, floatValue);
                break;
            case nameof(Double):
                if (double.TryParse(value, out double doubleValue))
                    customEvent.Add(key, doubleValue);
                break;
            default:
                customEvent.Add(key, value ?? string.Empty);
                break;
        }
    }
}
