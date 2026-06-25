using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Player item service (inventory — non-consumable, permanently owned items).
/// TItem is the project item enum (e.g. ItemId).
/// All mapping and cost logic is delegated to <see cref="IItemMapper{TItem,TCurrency}"/>.
/// <para>Sync failure while online → <see cref="InventoryOperationException"/>.</para>
/// </summary>
/// <typeparam name="TItem">Project enum of item identifiers.</typeparam>
public interface IItemService<TItem> where TItem : struct, Enum
{
    /// <summary>
    /// Whether the player owns the item (checks local cache, no server call).
    /// </summary>
    bool IsOwned(TItem id);

    /// <summary>
    /// Syncs the item list with the server.
    /// If offline — loads the last cache from PlayerPrefs without throwing.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Purchases an item: debits currency → grants the item on the server → updates cache.
    /// <para>
    /// If debit succeeded but grant failed — currency is rolled back automatically.
    /// </para>
    /// </summary>
    /// <returns>True if the purchase completed successfully.</returns>
    Task<bool> TryPurchaseAsync(TItem id, CancellationToken cancellationToken = default);
}
