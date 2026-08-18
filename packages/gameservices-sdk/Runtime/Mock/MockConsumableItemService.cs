using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Mock <see cref="IConsumableItemService{TItem}"/> implementation.
/// </summary>
public sealed class MockConsumableItemService<TItem> : IConsumableItemService<TItem>
    where TItem : struct, Enum
{
    readonly Dictionary<TItem, int> _quantities = new();

    /// <summary>Test helper: sets stack quantity (≤ 0 removes the entry) and raises <see cref="OnQuantityChanged"/>.</summary>
    public void SetQuantity(TItem id, int amount)
    {
        if (amount <= 0)
            _quantities.Remove(id);
        else
            _quantities[id] = amount;
        OnQuantityChanged?.Invoke(id, GetQuantity(id));
    }

    /// <inheritdoc/>
    public int GetQuantity(TItem id) =>
        _quantities.TryGetValue(id, out int value) ? value : 0;

    /// <inheritdoc/>
    public void ClearLocalCache()
    {
        _quantities.Clear();
        AppLog.DebugLog("MockConsumables", "ClearLocalCache.");
    }

    /// <inheritdoc/>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.DebugLog("MockConsumables", "Refresh (mock).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> TryConsumeAsync(
        TItem id,
        int amount = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (amount <= 0)
            return Task.FromResult(false);

        int current = GetQuantity(id);
        if (current < amount)
        {
            AppLog.DebugLog("MockConsumables", $"Consume {id} x{amount}: insufficient (have {current}).");
            return Task.FromResult(false);
        }

        SetQuantity(id, current - amount);
        AppLog.DebugLog("MockConsumables", $"Consume {id} x{amount} → {GetQuantity(id)}");
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<bool> TryGrantAsync(
        TItem id,
        int amount = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (amount <= 0)
            return Task.FromResult(false);

        SetQuantity(id, GetQuantity(id) + amount);
        AppLog.DebugLog("MockConsumables", $"Grant {id} x{amount} → {GetQuantity(id)}");
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public event Action<TItem, int> OnQuantityChanged;
}
