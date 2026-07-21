using System;

/// <summary>
/// Conflict between local and cloud save data.
/// Returned from <see cref="ICloudSaveService{TKey}.LoadAsync"/> or
/// <see cref="ICloudSaveService{TKey}.PushToCloudAsync"/> when local edits diverge from a
/// cloud version that moved since the last successful sync (<c>BaseTimestamp</c>).
/// <para>
/// The player must choose: <see cref="ICloudSaveService{TKey}.ApplyCloud"/> or
/// <see cref="ICloudSaveService{TKey}.KeepLocal"/> (then push again if keeping local).
/// </para>
/// </summary>
public readonly struct SaveConflict
{
    /// <summary>Time of the last local change (UTC).</summary>
    public readonly DateTime LocalTimestamp;

    /// <summary>Time of the last cloud save (UTC).</summary>
    public readonly DateTime CloudTimestamp;

    /// <summary>Which API detected the conflict.</summary>
    public readonly SaveConflictSource Source;

    /// <summary>True if the cloud save is newer than the local one.</summary>
    public bool IsCloudNewer => CloudTimestamp > LocalTimestamp;

    public SaveConflict(DateTime localTimestamp, DateTime cloudTimestamp, SaveConflictSource source)
    {
        LocalTimestamp = localTimestamp;
        CloudTimestamp = cloudTimestamp;
        Source = source;
    }
}
