using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// <see cref="IInventoryService{TCurrency}"/> implementation via Unity Gaming Services Economy SDK.
/// Default writes are deferred (optimistic <see cref="BalanceCache{TCurrency}"/> + durable
/// <see cref="PendingTransactionQueue{TCurrency}"/>) when the mapper allows the operation.
/// Flush via <see cref="FlushPendingAsync"/> / <see cref="RefreshBalancesAsync"/>.
/// Pass <c>syncImmediately: true</c> to force an online write. Timed-out immediate writes are
/// treated as indeterminate and reconciled against absolute server balances.
/// </summary>
/// <typeparam name="TCurrency">Project currency enum.</typeparam>
public sealed class UGSEconomyService<TCurrency> : IInventoryService<TCurrency>
    where TCurrency : struct, Enum
{
    readonly ICurrencyMapper<TCurrency> _mapper;
    readonly BalanceCache<TCurrency> _cache;
    readonly PendingTransactionQueue<TCurrency> _pendingQueue;
    readonly object _refreshGate = new object();
    Task _refreshTask;

    public UGSEconomyService(ICurrencyMapper<TCurrency> mapper)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _cache = new BalanceCache<TCurrency>();
        _cache.Load();
        _pendingQueue = new PendingTransactionQueue<TCurrency>(mapper);
    }

    /// <inheritdoc/>
    public long GetCachedBalance(TCurrency type) => _cache.Get(type);

    /// <inheritdoc/>
    public bool HasPendingTransactions => _pendingQueue.HasPending;

    /// <inheritdoc/>
    public EconomyRefreshResult LastRefreshResult { get; private set; }

    /// <inheritdoc/>
    public void ClearLocalCache()
    {
        _cache.Clear();
        _pendingQueue.Clear();
    }

    /// <inheritdoc/>
    public async Task FlushPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!NetworkStatus.IsOnline || !_pendingQueue.HasFlushablePending)
            return;

        await _pendingQueue.FlushAsync(_cache, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RefreshBalancesAsync(CancellationToken cancellationToken = default)
    {
        Task refresh;
        lock (_refreshGate)
        {
            if (_refreshTask != null && !_refreshTask.IsCompleted)
                refresh = _refreshTask;
            else
            {
                refresh = RefreshCoreAsync(cancellationToken);
                _refreshTask = refresh;
            }
        }

        try
        {
            await refresh;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_refreshGate)
            {
                if (ReferenceEquals(_refreshTask, refresh) && refresh.IsCompleted)
                    _refreshTask = null;
            }
        }
    }

    async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (!NetworkStatus.IsOnline)
        {
            _cache.Load();
            LastRefreshResult = EconomyRefreshResult.OfflineCache;
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _pendingQueue.FlushAsync(_cache, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Flushable pending still blocked — keep local until next refresh.
            // Unconfirmed rows must not block GetBalances (needed to resolve them).
            if (_pendingQueue.HasFlushablePending)
            {
                AppLog.Warn("Economy", "Pending queue not fully flushed — keeping local cache until next refresh.");
                _cache.Save();
                LastRefreshResult = EconomyRefreshResult.KeptLocalPending;
                return;
            }

            var result = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerBalances.GetBalancesAsync(),
                cancellationToken);
            _cache.UpdateFromServer(result.Balances, _mapper);
            _pendingQueue.ResolveUnconfirmed(_cache);
            _pendingQueue.ApplyPendingOnTop(_cache);
            _cache.Save();
            _cache.LogAll();
            NetworkStatus.ReportSuccess();
            LastRefreshResult = EconomyRefreshResult.ReachedServer;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InventoryOperationException)
        {
            LastRefreshResult = EconomyRefreshResult.TransportFallback;
            throw;
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e)
                                  || EconomyErrorClassifier.IsIndeterminate(e))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Economy", $"Refresh failed (transport) — using cached balances: {e.Message}");
            _cache.Load();
            LastRefreshResult = EconomyRefreshResult.TransportFallback;
        }
        catch (Exception e)
        {
            LastRefreshResult = EconomyRefreshResult.TransportFallback;
            throw new InventoryOperationException(
                InventoryFailureReason.ProviderRejected,
                "Failed to synchronize balances from server.",
                e);
        }
    }

    /// <inheritdoc/>
    public async Task AddCurrencyAsync(
        TCurrency type,
        int amount,
        CancellationToken cancellationToken = default,
        bool syncImmediately = false)
    {
        if (amount <= 0)
            return;

        if (ShouldDefer(type, InventoryOperation.Add, syncImmediately))
        {
            ApplyLocalDeltaOrThrow(type, amount, InventoryOperation.Add);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AppLog.Info("Economy", $"Deferred +{amount} {type} → {_cache.Get(type)} (queued)");
#endif
            return;
        }

        if (!NetworkStatus.IsOnline)
        {
            ApplyLocalDeltaOrThrow(type, amount, InventoryOperation.Add);
            return;
        }

        long before = _cache.Get(type);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerBalances
                    .IncrementBalanceAsync(_mapper.ToServiceId(type), amount),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(type, result.Balance);
            _cache.Save();
            NetworkStatus.ReportSuccess();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AppLog.Info("Economy", $"Applied online +{amount} {type} → {result.Balance}");
#endif
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (EconomyErrorClassifier.IsIndeterminate(e))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Economy", $"Add {type} indeterminate — reconciling before queue: {e.Message}");
            await ReconcileIndeterminateAddAsync(type, amount, before, cancellationToken);
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e)
                                  && _mapper.IsOfflineAllowed(type, InventoryOperation.Add))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Economy", $"Add {type} failed (recoverable) — queued locally: {e.Message}");
            ApplyLocalDelta(type, amount);
        }
        catch (Exception e)
        {
            AppLog.Error("Economy", $"Add failed {type}: {e.Message}");
            throw new InventoryOperationException(
                InventoryFailureReason.ProviderRejected,
                $"Failed to add {type}.",
                e);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TrySpendCurrencyAsync(
        TCurrency type,
        int amount,
        CancellationToken cancellationToken = default,
        bool syncImmediately = false)
    {
        if (amount <= 0)
            return false;

        if (ShouldDefer(type, InventoryOperation.Spend, syncImmediately))
        {
            if (!_mapper.IsOfflineAllowed(type, InventoryOperation.Spend))
            {
                AppLog.Warn("Economy", $"Spend {type} offline not allowed — returning false.");
                return false;
            }

            bool deferred = TryApplyLocalSpend(type, amount);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (deferred)
                AppLog.Info("Economy", $"Deferred -{amount} {type} → {_cache.Get(type)} (queued)");
#endif
            return deferred;
        }

        if (!NetworkStatus.IsOnline)
        {
            if (!_mapper.IsOfflineAllowed(type, InventoryOperation.Spend))
            {
                AppLog.Warn("Economy", $"Spend {type} offline not allowed — returning false.");
                return false;
            }

            return TryApplyLocalSpend(type, amount);
        }

        if (_cache.Get(type) < amount)
            return false;

        long before = _cache.Get(type);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerBalances
                    .DecrementBalanceAsync(_mapper.ToServiceId(type), amount),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _cache.Set(type, result.Balance);
            _cache.Save();
            NetworkStatus.ReportSuccess();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AppLog.Info("Economy", $"Applied online -{amount} {type} → {result.Balance}");
#endif
            return true;
        }
        catch (EconomyException e) when (e.Reason == EconomyExceptionReason.UnprocessableTransaction)
        {
            AppLog.Warn("Economy", $"Insufficient {type} per server — refreshing balance cache.");
            await RefreshBalancesAsync(cancellationToken);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (EconomyErrorClassifier.IsIndeterminate(e))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Economy", $"Spend {type} indeterminate — reconciling: {e.Message}");
            return await ReconcileIndeterminateSpendAsync(type, amount, before, cancellationToken);
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e)
                                  && _mapper.IsOfflineAllowed(type, InventoryOperation.Spend))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Economy", $"Spend {type} failed (recoverable) — queued locally: {e.Message}");
            return TryApplyLocalSpend(type, amount);
        }
        catch (Exception e) when (EconomyErrorClassifier.IsRecoverable(e))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Economy", $"Spend {type} failed (recoverable, offline spend disallowed): {e.Message}");
            return false;
        }
        catch (Exception e)
        {
            AppLog.Error("Economy", $"Spend failed {type}: {e.Message}");
            return false;
        }
    }

    bool ShouldDefer(TCurrency type, InventoryOperation operation, bool syncImmediately)
    {
        if (!_mapper.IsOfflineAllowed(type, operation))
            return false;

        // Escape hatch: force online write when the device is online.
        if (syncImmediately && NetworkStatus.IsOnline)
            return false;

        return true;
    }

    async Task ReconcileIndeterminateAddAsync(
        TCurrency type,
        int amount,
        long before,
        CancellationToken cancellationToken)
    {
        await TryForceServerSnapshotAsync(cancellationToken);

        if (_cache.Get(type) >= before + amount)
        {
            AppLog.Info("Economy", $"Indeterminate add {type} +{amount} already on server.");
            return;
        }

        if (_mapper.IsOfflineAllowed(type, InventoryOperation.Add))
        {
            AppLog.Warn("Economy", $"Indeterminate add {type} +{amount} missing on server — queuing locally.");
            ApplyLocalDelta(type, amount);
        }
    }

    async Task<bool> ReconcileIndeterminateSpendAsync(
        TCurrency type,
        int amount,
        long before,
        CancellationToken cancellationToken)
    {
        await TryForceServerSnapshotAsync(cancellationToken);

        if (_cache.Get(type) <= before - amount)
        {
            AppLog.Info("Economy", $"Indeterminate spend {type} -{amount} already on server.");
            return true;
        }

        if (_mapper.IsOfflineAllowed(type, InventoryOperation.Spend))
        {
            AppLog.Warn("Economy", $"Indeterminate spend {type} -{amount} missing on server — queuing locally.");
            return TryApplyLocalSpend(type, amount);
        }

        return false;
    }

    /// <summary>
    /// Best-effort GetBalances even during soft-offline, so timeout reconcile is not blind.
    /// </summary>
    async Task TryForceServerSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await NetworkRequest.WithTimeout(
                EconomyService.Instance.PlayerBalances.GetBalancesAsync(),
                cancellationToken);
            _cache.UpdateFromServer(result.Balances, _mapper);
            _pendingQueue.ResolveUnconfirmed(_cache);
            _pendingQueue.ApplyPendingOnTop(_cache);
            _cache.Save();
            NetworkStatus.ReportSuccess();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception refreshEx)
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Economy", $"Force snapshot after indeterminate write failed: {refreshEx.Message}");
            _cache.Load();
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
