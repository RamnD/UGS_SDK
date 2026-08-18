using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Mock <see cref="IItemService{TItem}"/> implementation.
/// </summary>
public sealed class MockItemService<TItem> : IItemService<TItem>
    where TItem : struct, Enum
{
    private readonly HashSet<TItem> _owned = new();

    /// <summary>Test helper: grants ownership without a purchase.</summary>
    public void GiveItem(TItem id)
    {
        _owned.Add(id);
        AppLog.DebugLog("MockItems", $"GiveItem: {id}");
    }

    /// <inheritdoc/>
    public bool IsOwned(TItem id) => _owned.Contains(id);

    /// <inheritdoc/>
    public void ClearLocalCache()
    {
        _owned.Clear();
        AppLog.DebugLog("MockItems", "ClearLocalCache.");
    }

    /// <inheritdoc/>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.DebugLog("MockItems", "Refresh (mock).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> TryPurchaseAsync(TItem id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_owned.Contains(id))
        {
            AppLog.DebugLog("MockItems", $"TryPurchase {id}: already owned.");
            return Task.FromResult(false);
        }

        _owned.Add(id);
        AppLog.DebugLog("MockItems", $"TryPurchase {id}: success (mock; currency not deducted).");
        return Task.FromResult(true);
    }
}
