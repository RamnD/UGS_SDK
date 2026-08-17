using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Read-only view of UGS Economy virtual and real-money purchase definitions for dynamic shop UI.
/// Does not execute purchases — use <see cref="IVirtualPurchaseService"/> and
/// <see cref="IRealMoneyPurchaseService"/> for that.
/// </summary>
/// <remarks>
/// SDK responsibility: Economy definition sync + queryable snapshot.
/// Game responsibility: online shop gate, presentation, localized IAP prices, purchase execution,
/// post-buy sync, and soft-currency shops that are not Economy Virtual Purchases.
/// </remarks>
public interface IEconomyPurchaseCatalog
{
    /// <summary>
    /// True after at least one successful <see cref="RefreshAsync"/> populated the in-memory cache.
    /// </summary>
    bool IsSynced { get; }

    /// <summary>
    /// Syncs Economy configuration from UGS and rebuilds the local catalog cache.
    /// When offline, or when sync fails after a prior success, keeps the last good cache.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns cached entries matching the query.
    /// When not yet synced, returns an empty list (check <see cref="IsSynced"/>).
    /// </summary>
    IReadOnlyList<PurchaseCatalogEntry> Query(PurchaseCatalogQuery query = default);

    /// <summary>Snapshot of all cached entries (empty when not synced).</summary>
    IReadOnlyList<PurchaseCatalogEntry> GetAll();

    /// <summary>Cached virtual purchases only.</summary>
    IReadOnlyList<PurchaseCatalogEntry> GetVirtual();

    /// <summary>Cached real-money purchases only.</summary>
    IReadOnlyList<PurchaseCatalogEntry> GetRealMoney();

    /// <summary>
    /// Looks up one entry by Economy purchase id (case-sensitive).
    /// </summary>
    bool TryGet(string purchaseId, out PurchaseCatalogEntry entry);
}
