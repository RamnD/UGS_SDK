using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Read-only view of UGS Economy virtual and real-money purchase definitions for dynamic shop UI.
/// Does not execute purchases — use <see cref="IVirtualPurchaseService"/> and
/// <see cref="IRealMoneyPurchaseService"/> for that.
/// </summary>
public interface IEconomyPurchaseCatalog
{
    /// <summary>
    /// True after at least one successful <see cref="RefreshAsync"/> populated the in-memory cache.
    /// </summary>
    bool IsSynced { get; }

    /// <summary>
    /// Syncs Economy configuration from UGS and rebuilds the local catalog cache.
    /// When offline, keeps serving the last successful cache if one exists.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns cached entries matching the query. Requires <see cref="IsSynced"/> unless the cache
    /// was populated by a prior successful refresh in this session.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown when the catalog was never refreshed and the cache is empty.
    /// </exception>
    IReadOnlyList<PurchaseCatalogEntry> Query(PurchaseCatalogQuery query = default);

    /// <summary>All cached entries (same as <c>Query(PurchaseCatalogQuery.All)</c>).</summary>
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
