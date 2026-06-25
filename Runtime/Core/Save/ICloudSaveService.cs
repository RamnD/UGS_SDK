using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Cloud save service with a local cache. TKey is the project key enum (e.g. SaveKey).
/// <para>
/// <b>Sync strategy:</b>
/// <list type="bullet">
/// <item><see cref="Set{TValue}"/> — writes to local cache only (memory + PlayerPrefs).</item>
/// <item><see cref="PushToCloudAsync"/> — uploads local cache to the cloud. Call
///   on app background/quit.</item>
/// <item><see cref="LoadAsync"/> — loads cloud data at start. If timestamps
///   differ — returns <see cref="SaveConflict"/> for the user to resolve.</item>
/// </list>
/// </para>
/// <para>
/// Network errors throw <see cref="CloudSaveOperationException"/> (the game can show retry).
/// </para>
/// </summary>
/// <typeparam name="TKey">Project enum of save keys.</typeparam>
public interface ICloudSaveService<TKey> where TKey : struct, Enum
{
    // ── Local access ──────────────────────────────────────────────────────

    /// <summary>UTC time of the last local change. Null if no data yet.</summary>
    DateTime? LocalTimestamp { get; }

    /// <summary>
    /// Reads a value from the local cache. Safe to call synchronously from UI/Update.
    /// </summary>
    /// <typeparam name="TValue">Value type (int, long, bool, string, or a serializable class).</typeparam>
    /// <param name="key">Save key.</param>
    /// <param name="defaultValue">Returned if the key is missing.</param>
    TValue Get<TValue>(TKey key, TValue defaultValue = default);

    /// <summary>
    /// Writes a value to the local cache and PlayerPrefs. Does not upload to the cloud.
    /// Call <see cref="PushToCloudAsync"/> to upload.
    /// </summary>
    void Set<TValue>(TKey key, TValue value);

    // ── Cloud sync ────────────────────────────────────────────────

    /// <summary>
    /// Loads data from the cloud.
    /// <list type="bullet">
    /// <item>No cloud data → returns null, local data unchanged.</item>
    /// <item>No local data → applies cloud, returns null.</item>
    /// <item>Both versions exist with different timestamps → returns <see cref="SaveConflict"/>.</item>
    /// </list>
    /// On conflict, data is unchanged until you explicitly call
    /// <see cref="ApplyCloud"/> or <see cref="KeepLocal"/>.
    /// </summary>
    Task<SaveConflict?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the local cache to the cloud with the current timestamp.
    /// Call from OnApplicationPause/OnApplicationQuit.
    /// </summary>
    Task PushToCloudAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the loaded cloud snapshot as local data.
    /// Call after the player chooses "use cloud save".
    /// </summary>
    void ApplyCloud();

    /// <summary>
    /// Keeps local data unchanged and discards the cloud snapshot.
    /// Call after the player chooses "use local save".
    /// </summary>
    void KeepLocal();
}
