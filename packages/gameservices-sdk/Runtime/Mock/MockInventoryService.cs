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

    /// <summary>Test helper: sets a currency balance without going through Add/Spend.</summary>
    public void SetBalance(TCurrency type, long amount) => _balances[type] = amount;

    /// <inheritdoc/>
    public long GetCachedBalance(TCurrency type) =>
        _balances.TryGetValue(type, out var value) ? value : 0;

    /// <inheritdoc/>
    public bool HasPendingTransactions => false;

    /// <inheritdoc/>
    public EconomyRefreshResult LastRefreshResult { get; private set; }

    /// <inheritdoc/>
    public void ClearLocalCache() => _balances.Clear();

    /// <inheritdoc/>
    public Task RefreshBalancesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRefreshResult = EconomyRefreshResult.ReachedServer;
        AppLog.DebugLog("MockEconomy", "RefreshBalances (mock).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FlushPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppLog.DebugLog("MockEconomy", "FlushPending (mock).");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AddCurrencyAsync(
        TCurrency type,
        int amount,
        CancellationToken cancellationToken = default,
        bool syncImmediately = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (amount <= 0) return Task.CompletedTask;
        _balances[type] = GetCachedBalance(type) + amount;
        AppLog.DebugLog("MockEconomy", $"Add {amount} {type} → {_balances[type]}" +
            (syncImmediately ? " (immediate)" : " (deferred)"));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> TrySpendCurrencyAsync(
        TCurrency type,
        int amount,
        CancellationToken cancellationToken = default,
        bool syncImmediately = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = GetCachedBalance(type);
        if (current < amount)
        {
            AppLog.DebugLog("MockEconomy", $"Spend {amount} {type}: insufficient (have {current}).");
            return Task.FromResult(false);
        }

        _balances[type] = current - amount;
        AppLog.DebugLog("MockEconomy", $"Spend {amount} {type} → {_balances[type]}" +
            (syncImmediately ? " (immediate)" : " (deferred)"));
        return Task.FromResult(true);
    }
}
