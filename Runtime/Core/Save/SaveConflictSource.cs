/// <summary>
/// Where a <see cref="SaveConflict"/> was detected.
/// </summary>
public enum SaveConflictSource
{
    /// <summary>Returned from <see cref="ICloudSaveService{TKey}.LoadAsync"/>.</summary>
    Load = 0,

    /// <summary>Returned from <see cref="ICloudSaveService{TKey}.PushToCloudAsync"/> (cloud moved since <c>BaseTimestamp</c>).</summary>
    Push = 1,
}
