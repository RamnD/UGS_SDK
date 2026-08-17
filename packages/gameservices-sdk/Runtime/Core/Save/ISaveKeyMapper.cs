using System;

/// <summary>
/// Save key mapper: converts the project enum to a cloud storage string key.
/// <para>
/// Implement in your project for each save-key enum.
/// Example: <c>SaveKeyMapper : ISaveKeyMapper&lt;SaveKey&gt;</c>
/// </para>
/// </summary>
/// <typeparam name="TKey">Project enum of save keys.</typeparam>
public interface ISaveKeyMapper<TKey> where TKey : struct, Enum
{
    /// <summary>
    /// Converts an enum key to a string for cloud and PlayerPrefs storage.
    /// Use snake_case: "high_score", "total_runs", etc.
    /// </summary>
    string ToCloudKey(TKey key);
}
