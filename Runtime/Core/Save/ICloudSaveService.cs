using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Cloud save service with a local cache. TKey is the project key enum (e.g. SaveKey).
/// <para>
/// <b>Sync strategy:</b>
/// <list type="bullet">
/// <item><see cref="Set{TValue}"/> — writes to local cache only (memory + PlayerPrefs).</item>
/// <item><see cref="PushToCloudAsync"/> — uploads local cache if it is based on the current
///   cloud version; returns <see cref="SaveConflict"/> if another client wrote first.</item>
/// <item><see cref="LoadAsync"/> — loads cloud data at start / reconnect. Returns
///   <see cref="SaveConflict"/> when local edits diverge from a newer cloud version.</item>
/// </list>
/// </para>
/// <para>
/// Conflicts are reported via <b>return values</b> (not events): await Load/Push, show UI,
/// then call <see cref="ApplyCloud"/> or <see cref="KeepLocal"/>.
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
    /// UTC cloud <c>__ts</c> from the last successful sync (load apply / push / conflict ack).
    /// Used as the optimistic-concurrency parent version. Null if never synced.
    /// </summary>
    DateTime? BaseTimestamp { get; }

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
    /// <item>Local clean and cloud ahead → applies cloud, returns null.</item>
    /// <item>Local dirty and cloud still at <see cref="BaseTimestamp"/> → keeps local, returns null.</item>
    /// <item>Local dirty and cloud moved → returns <see cref="SaveConflict"/>.</item>
    /// </list>
    /// On conflict, data is unchanged until you explicitly call
    /// <see cref="ApplyCloud"/> or <see cref="KeepLocal"/>.
    /// </summary>
    Task<SaveConflict?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the local cache to the cloud when local is dirty and cloud still matches
    /// <see cref="BaseTimestamp"/>. If another client wrote since the last sync, returns
    /// <see cref="SaveConflict"/> and does not upload.
    /// <para>
    /// Call from OnApplicationPause/OnApplicationQuit. If a conflict is returned, show UI,
    /// resolve with <see cref="ApplyCloud"/> / <see cref="KeepLocal"/>, then push again
    /// when keeping local.
    /// </para>
    /// </summary>
    Task<SaveConflict?> PushToCloudAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the loaded cloud snapshot as local data.
    /// Call after the player chooses "use cloud save".
    /// </summary>
    void ApplyCloud();

    /// <summary>
    /// Keeps local data and discards the cloud snapshot.
    /// Acknowledges the conflicting cloud version so the next <see cref="PushToCloudAsync"/>
    /// can overwrite it. Call after the player chooses "use local save", then push again.
    /// </summary>
    void KeepLocal();
}
