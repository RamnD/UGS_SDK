using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Mock-реализация <see cref="IItemService{TItem}"/>.
/// </summary>
public sealed class MockItemService<TItem> : IItemService<TItem>
    where TItem : struct, Enum
{
    private readonly HashSet<TItem> _owned = new();

    public void GiveItem(TItem id)
    {
        _owned.Add(id);
        Debug.Log($"[Mock Items] GiveItem: {id}");
    }

    /// <inheritdoc/>
    public bool IsOwned(TItem id) => _owned.Contains(id);

    /// <inheritdoc/>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Debug.Log("[Mock Items] Refresh (mock).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> TryPurchaseAsync(TItem id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_owned.Contains(id))
        {
            Debug.Log($"[Mock Items] TryPurchase {id}: already owned.");
            return Task.FromResult(false);
        }

        _owned.Add(id);
        Debug.Log($"[Mock Items] TryPurchase {id}: success (mock; currency not deducted).");
        return Task.FromResult(true);
    }
}
