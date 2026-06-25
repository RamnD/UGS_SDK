using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Stackable consumable item service (quantity, consume, grant).
/// Does not replace <see cref="IItemService{TItem}"/> — permanent unlocks stay there.
/// <para>Sync failure while online → <see cref="InventoryOperationException"/>.</para>
/// </summary>
/// <typeparam name="TItem">Project enum of items (e.g. ItemId).</typeparam>
public interface IConsumableItemService<TItem> where TItem : struct, Enum
{
    /// <summary>
    /// Current quantity from local cache (no server call).
    /// Safe to call from Update / UI.
    /// </summary>
    int GetQuantity(TItem id);

    /// <summary>
    /// Syncs quantities with the server.
    /// If offline — loads the last cache from PlayerPrefs without throwing.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes <paramref name="amount"/> units.
    /// Returns false on insufficient quantity, offline, or provider rejection (no exception for soft failures).
    /// </summary>
    Task<bool> TryConsumeAsync(
        TItem id,
        int amount = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants <paramref name="amount"/> units (purchase, reward, dev-grant).
    /// </summary>
    Task<bool> TryGrantAsync(
        TItem id,
        int amount = 1,
        CancellationToken cancellationToken = default);

    /// <summary>Fires after quantity changes (consume, grant, refresh).</summary>
    event Action<TItem, int> OnQuantityChanged;
}
