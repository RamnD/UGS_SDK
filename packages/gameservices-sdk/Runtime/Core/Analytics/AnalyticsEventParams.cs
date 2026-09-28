using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Shared reflection for <see cref="AnalyticsKeyAttribute"/> fields on event structs.
/// Used by UGS serialization and RamnD gateway envelope building.
/// </summary>
internal static class AnalyticsEventParams
{
    public static Dictionary<string, object> ToDictionary<T>(T dataObject) where T : struct
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
}
