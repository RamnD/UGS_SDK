using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// <see cref="IInventoryService{TCurrency}"/> implementation via Unity Gaming Services Economy SDK.
/// </summary>
/// <typeparam name="TCurrency">Project currency enum.</typeparam>
public sealed class UGSEconomyService<TCurrency> : IInventoryService<TCurrency>
    where TCurrency : struct, Enum
{
    private readonly ICurrencyMapper<TCurrency>         _mapper;
    private readonly BalanceCache<TCurrency>            _cache;
    private readonly PendingTransactionQueue<TCurrency> _pendingQueue;

    public UGSEconomyService(ICurrencyMapper<TCurrency> mapper)
    {
        _mapper       = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _cache        = new BalanceCache<TCurrency>();
        _pendingQueue = new PendingTransactionQueue<TCurrency>(mapper);
    }

    /// <inheritdoc/>
    public long GetCachedBalance(TCurrency type) => _cache.Get(type);

    /// <inheritdoc/>
    public async Task RefreshBalancesAsync(CancellationToken cancellationToken = default)
    {
        if (!NetworkStatus.IsOnline)
        {
            _cache.Load();
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _pendingQueue.FlushAsync(_cache, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EconomyService.Instance.PlayerBalances.GetBalancesAsync();
            _cache.UpdateFromServer(result.Balances, _mapper);
            _cache.LogAll();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InventoryOperationException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new InventoryOperationException(
                InventoryFailureReason.ProviderRejected,
                "Failed to synchronize balances from server.",
                e);
        }
    }

    /// <inheritdoc/>
    public async Task AddCurrencyAsync(TCurrency type, int amount,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return;

        if (!NetworkStatus.IsOnline)
        {
            if (!_mapper.IsOfflineAllowed(type, InventoryOperation.Add))
            {
                throw new InventoryOperationException(
                    InventoryFailureReason.OperationNotAllowedOffline,
                    $"{type}: credit offline not allowed per currency mapper.");
            }

            _cache.Set(type, _cache.Get(type) + amount);
            _pendingQueue.Enqueue(type, amount);
            _cache.Save();
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EconomyService.Instance.PlayerBalances
                .IncrementBalanceAsync(_mapper.ToServiceId(type), amount);
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(type, result.Balance);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Economy] Add failed {type}: {e.Message}");
            throw new InventoryOperationException(
                InventoryFailureReason.ProviderRejected,
                $"Failed to add {type}.",
                e);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TrySpendCurrencyAsync(TCurrency type, int amount,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0) return false;

        if (!NetworkStatus.IsOnline)
        {
            if (!_mapper.IsOfflineAllowed(type, InventoryOperation.Spend))
            {
                throw new InventoryOperationException(
                    InventoryFailureReason.OperationNotAllowedOffline,
                    $"{type}: spend offline not allowed per currency mapper.");
            }

            if (_cache.Get(type) < amount)
                return false;

            _cache.Set(type, _cache.Get(type) - amount);
            _pendingQueue.Enqueue(type, -amount);
            _cache.Save();
            return true;
        }

        if (_cache.Get(type) < amount) return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EconomyService.Instance.PlayerBalances
                .DecrementBalanceAsync(_mapper.ToServiceId(type), amount);
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(type, result.Balance);
            return true;
        }
        catch (EconomyException e) when (e.Reason == EconomyExceptionReason.UnprocessableTransaction)
        {
            Debug.LogWarning($"[Economy] Insufficient {type} per server — refreshing balance cache.");
            await RefreshBalancesAsync(cancellationToken);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InventoryOperationException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new InventoryOperationException(
                InventoryFailureReason.ProviderRejected,
                $"Spend failed for {type} (network or server error).",
                e);
        }
    }
}
