using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// In-memory <see cref="IEconomyPurchaseCatalog"/> for editor / offline tests.
/// </summary>
public sealed class MockEconomyPurchaseCatalog : IEconomyPurchaseCatalog
{
    readonly List<PurchaseCatalogEntry> _entries = new();
    readonly Dictionary<string, PurchaseCatalogEntry> _byId = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool IsSynced { get; private set; }

    /// <summary>Replaces the mock catalog contents.</summary>
    public void SetEntries(IEnumerable<PurchaseCatalogEntry> entries)
    {
        _entries.Clear();
        _byId.Clear();

        if (entries == null)
            return;

        foreach (var entry in entries)
        {
            _entries.Add(entry);
            _byId[entry.Id] = entry;
        }
    }

    /// <inheritdoc/>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsSynced = true;
        Debug.Log($"[Mock PurchaseCatalog] Refresh — {_entries.Count} entr(y/ies).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IReadOnlyList<PurchaseCatalogEntry> Query(PurchaseCatalogQuery query = default)
    {
        EnsureReadable();
        return PurchaseCatalogFiltering.Apply(_entries, query);
    }

    /// <inheritdoc/>
    public IReadOnlyList<PurchaseCatalogEntry> GetAll()
    {
        EnsureReadable();
        return _entries;
    }

    /// <inheritdoc/>
    public IReadOnlyList<PurchaseCatalogEntry> GetVirtual()
    {
        EnsureReadable();
        return _entries.Where(e => e.Kind == PurchaseCatalogKind.Virtual).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<PurchaseCatalogEntry> GetRealMoney()
    {
        EnsureReadable();
        return _entries.Where(e => e.Kind == PurchaseCatalogKind.RealMoney).ToList();
    }

    /// <inheritdoc/>
    public bool TryGet(string purchaseId, out PurchaseCatalogEntry entry)
    {
        entry = null;
        if (!IsSynced || string.IsNullOrEmpty(purchaseId))
            return false;

        return _byId.TryGetValue(purchaseId, out entry);
    }

    void EnsureReadable()
    {
        if (!IsSynced)
            throw new InvalidOperationException(
                "Mock purchase catalog is not synced. Call RefreshAsync() or SetEntries() then RefreshAsync().");
    }
}
