using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using Unity.Services.Economy.Model;
using UnityEngine;

/// <summary>
/// <see cref="IEconomyPurchaseCatalog"/> backed by UGS Economy configuration
/// (virtual + real-money purchase definitions).
/// </summary>
public sealed class UGSEconomyPurchaseCatalog : IEconomyPurchaseCatalog
{
    const int SyncTimeoutMs = 15000;

    readonly object _syncGate = new();

    List<PurchaseCatalogEntry> _entries = new();
    Dictionary<string, PurchaseCatalogEntry> _byId = new(StringComparer.Ordinal);
    Task _syncTask;
    bool _isSynced;

    /// <inheritdoc/>
    public bool IsSynced => _isSynced;

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!NetworkStatus.IsOnline)
        {
            if (_isSynced)
            {
                Debug.LogWarning("[SDK][PurchaseCatalog] Offline — using last cached catalog.");
                return;
            }

            Debug.LogWarning("[SDK][PurchaseCatalog] Offline and catalog was never synced.");
            return;
        }

        await EnsureConfigurationSyncedAsync(cancellationToken);
        RebuildCacheFromConfiguration();
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
    public IReadOnlyList<PurchaseCatalogEntry> GetVirtual() =>
        Query(new PurchaseCatalogQuery { Kind = PurchaseCatalogKind.Virtual });

    /// <inheritdoc/>
    public IReadOnlyList<PurchaseCatalogEntry> GetRealMoney() =>
        Query(new PurchaseCatalogQuery { Kind = PurchaseCatalogKind.RealMoney });

    /// <inheritdoc/>
    public bool TryGet(string purchaseId, out PurchaseCatalogEntry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(purchaseId) || !_isSynced)
            return false;

        return _byId.TryGetValue(purchaseId, out entry);
    }

    void EnsureReadable()
    {
        if (!_isSynced)
            throw new InvalidOperationException(
                "Purchase catalog is not synced. Call RefreshAsync() after authentication and before Query().");
    }

    async Task EnsureConfigurationSyncedAsync(CancellationToken cancellationToken)
    {
        Task syncTask;
        lock (_syncGate)
        {
            if (_syncTask != null && !_syncTask.IsCompleted)
            {
                syncTask = _syncTask;
            }
            else
            {
                syncTask = SyncConfigurationCoreAsync(cancellationToken);
                _syncTask = syncTask;
            }
        }

        try
        {
            await syncTask;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_syncGate)
            {
                if (ReferenceEquals(_syncTask, syncTask) && syncTask.IsCompleted)
                    _syncTask = null;
            }
        }
    }

    async Task SyncConfigurationCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await NetworkRequest.WithTimeout(
                EconomyService.Instance.Configuration.SyncConfigurationAsync(),
                cancellationToken,
                timeoutMs: SyncTimeoutMs);
            NetworkStatus.ReportSuccess();
        }
        catch (Exception ex)
        {
            if (_isSynced)
            {
                Debug.LogWarning(
                    $"[SDK][PurchaseCatalog] Config sync failed — keeping last cached catalog: {ex.Message}");
                return;
            }

            NetworkStatus.ReportFailure();
            Debug.LogError($"[SDK][PurchaseCatalog] Config sync failed: {ex}");
            throw;
        }
    }

    void RebuildCacheFromConfiguration()
    {
        var configuration = EconomyService.Instance.Configuration;
        var entries = new List<PurchaseCatalogEntry>();
        var byId = new Dictionary<string, PurchaseCatalogEntry>(StringComparer.Ordinal);

        foreach (var definition in configuration.GetVirtualPurchases())
        {
            var entry = MapVirtualPurchase(definition);
            entries.Add(entry);
            byId[entry.Id] = entry;
        }

        foreach (var definition in configuration.GetRealMoneyPurchases())
        {
            var entry = MapRealMoneyPurchase(definition);
            entries.Add(entry);
            byId[entry.Id] = entry;
        }

        _entries = entries;
        _byId = byId;
        _isSynced = true;
        Debug.Log($"[SDK][PurchaseCatalog] Cached {entries.Count} purchase definition(s).");
    }

    static PurchaseCatalogEntry MapVirtualPurchase(VirtualPurchaseDefinition definition)
    {
        return new PurchaseCatalogEntry(
            definition.Id,
            definition.Name,
            PurchaseCatalogKind.Virtual,
            MapLines(definition.Costs),
            MapLines(definition.Rewards));
    }

    static PurchaseCatalogEntry MapRealMoneyPurchase(RealMoneyPurchaseDefinition definition)
    {
        var storeIds = definition.StoreIdentifiers;
        return new PurchaseCatalogEntry(
            definition.Id,
            definition.Name,
            PurchaseCatalogKind.RealMoney,
            Array.Empty<PurchaseCatalogLine>(),
            MapLines(definition.Rewards),
            storeIds?.AppleAppStore,
            storeIds?.GooglePlayStore);
    }

    static IReadOnlyList<PurchaseCatalogLine> MapLines(IReadOnlyList<PurchaseItemQuantity> lines)
    {
        if (lines == null || lines.Count == 0)
            return Array.Empty<PurchaseCatalogLine>();

        var mapped = new PurchaseCatalogLine[lines.Count];
        for (var i = 0; i < lines.Count; i++)
            mapped[i] = MapLine(lines[i]);

        return mapped;
    }

    static PurchaseCatalogLine MapLine(PurchaseItemQuantity quantity)
    {
        if (quantity?.Item == null)
            return new PurchaseCatalogLine(string.Empty, quantity?.Amount ?? 0, PurchaseResourceKind.Unknown);

        var referenced = quantity.Item.GetReferencedConfigurationItem();
        if (referenced == null)
            return new PurchaseCatalogLine(string.Empty, quantity.Amount, PurchaseResourceKind.Unknown);

        var kind = referenced.Type switch
        {
            "CURRENCY" => PurchaseResourceKind.Currency,
            "INVENTORY_ITEM" => PurchaseResourceKind.InventoryItem,
            _ => PurchaseResourceKind.Unknown,
        };

        return new PurchaseCatalogLine(referenced.Id, quantity.Amount, kind);
    }

}
