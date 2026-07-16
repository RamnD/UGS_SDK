using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// <see cref="IInventoryService{TCurrency}"/> implementation via Unity Gaming Services Economy SDK.
/// Offline and recoverable network failures use optimistic <see cref="BalanceCache{TCurrency}"/>
/// plus a durable <see cref="PendingTransactionQueue{TCurrency}"/> flushed on
/// <see cref="RefreshBalancesAsync"/>.
/// </summary>
/// <typeparam name="TCurrency">Project currency enum.</typeparam>
public sealed class UGSEconomyService<TCurrency> : IInventoryService<TCurrency>
    where TCurrency : struct, Enum
{
    readonly ICurrencyMapper<TCurrency> _mapper;
    readonly BalanceCache<TCurrency> _cache;
    readonly PendingTransactionQueue<TCurrency> _pendingQueue;

    public UGSEconomyService(ICurrencyMapper<TCurrency> mapper)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _cache = new BalanceCache<TCurrency>();
        _cache.Load(); // PlayerPrefs — so offline / queued ops start from last known balances
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

            // Do not overwrite optimistic local cache while pending deltas remain.
            if (_pendingQueue.HasPending)
            {
                Debug.LogWarning(
                    "[Economy] Pending queue not fully flushed — keeping local cache until next refresh.");
                _cache.Save();
                return;
            }

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
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
        {
            Debug.LogWarning($"[Economy] Refresh failed (recoverable) — using cached balances: {e.Message}");
            _cache.Load();
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
        if (amount <= 0)
            return;

        if (!NetworkStatus.IsOnline)
        {
            ApplyLocalDeltaOrThrow(type, amount, InventoryOperation.Add);
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EconomyService.Instance.PlayerBalances
                .IncrementBalanceAsync(_mapper.ToServiceId(type), amount);
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(type, result.Balance);
            _cache.Save();
            Debug.Log($"[Economy] Applied online +{amount} {type} → {result.Balance}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e)
                                  && _mapper.IsOfflineAllowed(type, InventoryOperation.Add))
        {
            Debug.LogWarning(
                $"[Economy] Add {type} failed (recoverable) — queued locally: {e.Message}");
            ApplyLocalDelta(type, amount);
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
        if (amount <= 0)
            return false;

        if (!NetworkStatus.IsOnline)
        {
            if (!_mapper.IsOfflineAllowed(type, InventoryOperation.Spend))
            {
                Debug.LogWarning($"[Economy] Spend {type} offline not allowed — returning false.");
                return false;
            }

            return TryApplyLocalSpend(type, amount);
        }

        if (_cache.Get(type) < amount)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EconomyService.Instance.PlayerBalances
                .DecrementBalanceAsync(_mapper.ToServiceId(type), amount);
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(type, result.Balance);
            _cache.Save();
            Debug.Log($"[Economy] Applied online -{amount} {type} → {result.Balance}");
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
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e)
                                  && _mapper.IsOfflineAllowed(type, InventoryOperation.Spend))
        {
            Debug.LogWarning(
                $"[Economy] Spend {type} failed (recoverable) — queued locally: {e.Message}");
            return TryApplyLocalSpend(type, amount);
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
        {
            Debug.LogWarning(
                $"[Economy] Spend {type} failed (recoverable, offline spend disallowed): {e.Message}");
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Economy] Spend failed {type}: {e.Message}");
            return false;
        }
    }

    void ApplyLocalDeltaOrThrow(TCurrency type, int signedAmount, InventoryOperation operation)
    {
        if (!_mapper.IsOfflineAllowed(type, operation))
        {
            throw new InventoryOperationException(
                InventoryFailureReason.OperationNotAllowedOffline,
                $"{type}: {operation} offline not allowed per currency mapper.");
        }

        ApplyLocalDelta(type, signedAmount);
    }

    void ApplyLocalDelta(TCurrency type, int signedAmount)
    {
        _cache.Set(type, _cache.Get(type) + signedAmount);
        _pendingQueue.Enqueue(type, signedAmount);
        _cache.Save();
    }

    bool TryApplyLocalSpend(TCurrency type, int amount)
    {
        if (_cache.Get(type) < amount)
            return false;

        ApplyLocalDelta(type, -amount);
        return true;
    }
}
