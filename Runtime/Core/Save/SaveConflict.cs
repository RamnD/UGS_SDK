using System;

/// <summary>
/// Conflict between local and cloud save data.
/// Returned from <see cref="ICloudSaveService{TKey}.LoadAsync"/> when both versions exist
/// with different timestamps.
/// <para>
/// The player must choose: <see cref="ICloudSaveService{TKey}.ApplyCloud"/> or
/// <see cref="ICloudSaveService{TKey}.KeepLocal"/>.
/// </para>
/// </summary>
public readonly struct SaveConflict
{
    /// <summary>Time of the last local change (UTC).</summary>
    public readonly DateTime LocalTimestamp;

    /// <summary>Time of the last cloud save (UTC).</summary>
    public readonly DateTime CloudTimestamp;

    /// <summary>True if the cloud save is newer than the local one.</summary>
    public bool IsCloudNewer => CloudTimestamp > LocalTimestamp;

    public SaveConflict(DateTime localTimestamp, DateTime cloudTimestamp)
    {
        LocalTimestamp = localTimestamp;
        CloudTimestamp = cloudTimestamp;
    }
}
