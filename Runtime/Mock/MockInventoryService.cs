using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Mock <see cref="IInventoryService{TCurrency}"/> implementation.
/// </summary>
public sealed class MockInventoryService<TCurrency> : IInventoryService<TCurrency>
    where TCurrency : struct, Enum
{
    private readonly Dictionary<TCurrency, long> _balances = new();

    public void SetBalance(TCurrency type, long amount) => _balances[type] = amount;

    /// <inheritdoc/>
    public long GetCachedBalance(TCurrency type) =>
        _balances.TryGetValue(type, out var value) ? value : 0;

    /// <inheritdoc/>
    public Task RefreshBalancesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Debug.Log("[Mock Economy] RefreshBalances (mock).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AddCurrencyAsync(TCurrency type, int amount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (amount <= 0) return Task.CompletedTask;
        _balances[type] = GetCachedBalance(type) + amount;
        Debug.Log($"[Mock Economy] Add {amount} {type} → {_balances[type]}");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> TrySpendCurrencyAsync(TCurrency type, int amount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = GetCachedBalance(type);
        if (current < amount)
        {
            Debug.Log($"[Mock Economy] Spend {amount} {type}: insufficient (have {current}).");
            return Task.FromResult(false);
        }

        _balances[type] = current - amount;
        Debug.Log($"[Mock Economy] Spend {amount} {type} → {_balances[type]}");
        return Task.FromResult(true);
    }
}
