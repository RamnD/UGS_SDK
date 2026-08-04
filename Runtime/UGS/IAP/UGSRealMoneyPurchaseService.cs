using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using Unity.Services.Economy.Model;
using UnityEngine;
using UnityEngine.Purchasing;

/// <summary>
/// Unity IAP + UGS Economy bridge for portable real-money purchases.
/// Unity IAP uses <see cref="RealMoneyProductDefinition.ResolvedStoreProductId"/>;
/// Economy redeem uses <see cref="RealMoneyProductDefinition.ProductId"/>.
/// </summary>
public sealed class UGSRealMoneyPurchaseService<TKey, TCurrency> : IRealMoneyPurchaseService
    where TKey : struct, Enum
    where TCurrency : struct, Enum
{
    [Serializable]
    sealed class GoogleReceiptPayload
    {
        public string json;
        public string signature;
    }

    readonly ICloudSaveService<TKey> _cloudSave;
    readonly IInventoryService<TCurrency> _economy;
    readonly CloudSaveEntitlementStore<TKey> _entitlements;
    readonly Dictionary<string, TaskCompletionSource<bool>> _purchaseRequests = new(StringComparer.Ordinal);
    readonly Dictionary<string, RealMoneyProductDefinition> _productsById = new(StringComparer.Ordinal);
    readonly Dictionary<string, RealMoneyProductDefinition> _productsByStoreId = new(StringComparer.Ordinal);
    readonly HashSet<string> _txInFlightOrDone = new(StringComparer.Ordinal);
    readonly object _pendingGate = new object();

    StoreController _storeController;
    bool _isInitialized;
    bool _fetchRequested;
    bool _areProductsReady;
    bool _lastPurchaseWasUserCancelled;
    bool _lastPurchaseGrantedRewards;
    bool _lastRedeemIndeterminate;
    RealMoneyPurchaseOutcome _lastPurchaseOutcome;
    string _activePurchaseProductId;
    Task _pendingDrainTask;
    TaskCompletionSource<bool> _purchasesFetchTcs;
    TaskCompletionSource<RestorePurchasesResult> _restorePurchasesTcs;
    int _pendingHandlersInFlight;

    public bool IsInitialized => _isInitialized;

    public bool AreProductsReady => _areProductsReady;

    public RealMoneyPurchaseOutcome LastPurchaseOutcome => _lastPurchaseOutcome;

    public bool LastPurchaseWasUserCancelled => _lastPurchaseWasUserCancelled;

    public bool LastPurchaseGrantedRewards => _lastPurchaseGrantedRewards;

    public event Action<string> PurchaseSucceeded;

    public event Action ProductsUpdated;

    public UGSRealMoneyPurchaseService(
        ICloudSaveService<TKey> cloudSave,
        TKey entitlementSaveKey,
        IInventoryService<TCurrency> economy = null)
    {
        _cloudSave = cloudSave ?? throw new ArgumentNullException(nameof(cloudSave));
        _economy = economy;
        _entitlements = new CloudSaveEntitlementStore<TKey>(cloudSave, entitlementSaveKey);
    }

    public async Task InitializeAsync(
        RealMoneyProductDefinition[] products,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return;

        if (products == null || products.Length == 0)
            throw new ArgumentException("At least one real-money product is required.", nameof(products));

        foreach (RealMoneyProductDefinition product in products)
        {
            if (product == null || string.IsNullOrWhiteSpace(product.ProductId))
                throw new ArgumentException("Each real-money product must have a non-empty ProductId.", nameof(products));

            string storeId = product.ResolvedStoreProductId;
            if (string.IsNullOrWhiteSpace(storeId))
                throw new ArgumentException($"Product '{product.ProductId}' has an empty store product id.", nameof(products));

            if (_productsByStoreId.ContainsKey(storeId) &&
                !string.Equals(_productsByStoreId[storeId].ProductId, product.ProductId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Store product id '{storeId}' is registered for more than one Economy product.",
                    nameof(products));
            }

            _productsById[product.ProductId] = product;
            _productsByStoreId[storeId] = product;
        }

        _storeController = UnityIAPServices.StoreController();
        _storeController.OnPurchasePending += OnPurchasePending;
        _storeController.OnPurchaseConfirmed += OnPurchaseConfirmed;
        _storeController.OnPurchaseFailed += OnPurchaseFailed;
        _storeController.OnPurchasesFetched += OnPurchasesFetched;
        _storeController.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
        _storeController.OnProductsFetched += OnProductsFetched;
        _storeController.OnProductsFetchFailed += OnProductsFetchFailed;

        await _storeController.Connect();

        // StoreKit 2 no longer auto-updates the legacy App Receipt that Economy redeem needs.
        // Keep Unity's post-purchase refresh enabled so order.Info.Apple.AppReceipt can catch up.
        _storeController.AppleStoreExtendedPurchaseService?.SetRefreshAppReceipt(true);

        FetchProducts();
        _isInitialized = true;
        Debug.Log("[SDK][IAP] Store connected — draining pending purchases...");
        await ProcessPendingPurchasesAsync(cancellationToken);
        Debug.Log("[SDK][IAP] Pending purchase drain finished.");
    }

    public void EnsureProductsFetched()
    {
        if (!_isInitialized || _storeController == null || _areProductsReady)
            return;

        // Allow a fresh request after failure or if the first FetchProducts never completed.
        _fetchRequested = false;
        FetchProducts();
    }

    public async Task ProcessPendingPurchasesAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized || _storeController == null)
            return;

        Task drain;
        lock (_pendingGate)
        {
            if (_pendingDrainTask != null && !_pendingDrainTask.IsCompleted)
                drain = _pendingDrainTask;
            else
            {
                drain = DrainPendingPurchasesCoreAsync(cancellationToken);
                _pendingDrainTask = drain;
            }
        }

        try
        {
            await drain;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_pendingGate)
            {
                if (ReferenceEquals(_pendingDrainTask, drain) && drain.IsCompleted)
                    _pendingDrainTask = null;
            }
        }
    }

    async Task DrainPendingPurchasesCoreAsync(CancellationToken cancellationToken)
    {
        var fetchTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingGate)
            _purchasesFetchTcs = fetchTcs;

        try
        {
            _storeController.FetchPurchases();

            Task completed = await Task.WhenAny(fetchTcs.Task, Task.Delay(20000, cancellationToken));
            if (completed != fetchTcs.Task)
            {
                Debug.LogWarning("[SDK][IAP] FetchPurchases timed out while draining pending orders.");
                fetchTcs.TrySetResult(false);
            }
            else
            {
                await fetchTcs.Task;
            }

            // Wait for any OnPurchasePending handlers kicked by the fetch / store.
            await WaitForPendingHandlersIdleAsync(cancellationToken);
        }
        finally
        {
            lock (_pendingGate)
            {
                if (ReferenceEquals(_purchasesFetchTcs, fetchTcs))
                    _purchasesFetchTcs = null;
            }
        }
    }

    async Task WaitForPendingHandlersIdleAsync(CancellationToken cancellationToken)
    {
        const int maxWaitMs = 60000;
        int waited = 0;
        while (Volatile.Read(ref _pendingHandlersInFlight) > 0 && waited < maxWaitMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(100, cancellationToken);
            waited += 100;
        }

        if (Volatile.Read(ref _pendingHandlersInFlight) > 0)
            Debug.LogWarning("[SDK][IAP] Pending purchase handlers still running after drain wait.");
    }

    public async Task<bool> PurchaseAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        _lastPurchaseWasUserCancelled = false;
        _lastPurchaseGrantedRewards = false;
        _lastRedeemIndeterminate = false;
        _lastPurchaseOutcome = RealMoneyPurchaseOutcome.None;
        _activePurchaseProductId = null;

        if (!_isInitialized || _storeController == null)
            throw new InvalidOperationException("InitializeAsync must complete before PurchaseAsync.");

        if (!_productsById.TryGetValue(productId, out RealMoneyProductDefinition definition))
            throw new InvalidOperationException($"Product '{productId}' is not registered in this purchase service.");

        // Single-flight: overlapping purchases would race LastPurchaseWasUserCancelled
        // and leave orphan store sheets / TCS entries.
        if (_purchaseRequests.Count > 0)
        {
            Debug.LogWarning(
                $"[SDK][IAP] Purchase rejected for '{productId}' — another purchase is already in flight.");
            _lastPurchaseOutcome = RealMoneyPurchaseOutcome.Failed;
            return false;
        }

        // Clear stuck store transactions before opening a new sheet.
        try
        {
            await ProcessPendingPurchasesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SDK][IAP] Pre-purchase pending drain failed: {ex.Message}");
        }

        string storeId = definition.ResolvedStoreProductId;
        Product product = FindStoreProduct(storeId);
        if (product == null)
        {
            EnsureProductsFetched();
            Debug.LogWarning(
                $"[SDK][IAP] Product '{productId}' (store id '{storeId}') not fetched from the store — refetch kicked.");
            _lastPurchaseOutcome = RealMoneyPurchaseOutcome.Failed;
            return false;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _purchaseRequests[productId] = tcs;
        _activePurchaseProductId = productId;
        _storeController.PurchaseProduct(product);

        using CancellationTokenRegistration ctr = cancellationToken.Register(() =>
        {
            if (_purchaseRequests.Remove(productId))
                tcs.TrySetCanceled(cancellationToken);
        });

        try
        {
            bool ok = await tcs.Task;
            FinalizePurchaseOutcome(ok);
            // Rewards may have landed from stuck-pending recovery even if the awaiter saw false.
            return ok || _lastPurchaseGrantedRewards;
        }
        catch (OperationCanceledException)
        {
            _lastPurchaseOutcome = RealMoneyPurchaseOutcome.Cancelled;
            throw;
        }
        finally
        {
            if (string.Equals(_activePurchaseProductId, productId, StringComparison.Ordinal))
                _activePurchaseProductId = null;
        }
    }

    void FinalizePurchaseOutcome(bool success)
    {
        if (success)
        {
            _lastPurchaseOutcome = RealMoneyPurchaseOutcome.Success;
            return;
        }

        if (_lastPurchaseWasUserCancelled)
        {
            _lastPurchaseOutcome = RealMoneyPurchaseOutcome.Cancelled;
            return;
        }

        // Grant already applied (e.g. stuck pending redeemed while waiting) — treat as soft success.
        if (_lastPurchaseGrantedRewards)
        {
            _lastPurchaseOutcome = RealMoneyPurchaseOutcome.Success;
            return;
        }

        if (_lastRedeemIndeterminate)
        {
            _lastPurchaseOutcome = RealMoneyPurchaseOutcome.Indeterminate;
            return;
        }

        _lastPurchaseOutcome = RealMoneyPurchaseOutcome.Failed;
    }

    public void RestorePurchases() =>
        _ = RestorePurchasesAsync(CancellationToken.None);

    public async Task<RestorePurchasesResult> RestorePurchasesAsync(
        CancellationToken cancellationToken = default)
    {
        if (_storeController == null)
            throw new InvalidOperationException("InitializeAsync must complete before RestorePurchasesAsync.");

        TaskCompletionSource<RestorePurchasesResult> restoreTcs;
        bool startRestore = false;
        lock (_pendingGate)
        {
            if (_restorePurchasesTcs != null && !_restorePurchasesTcs.Task.IsCompleted)
                restoreTcs = _restorePurchasesTcs;
            else
            {
                restoreTcs = new TaskCompletionSource<RestorePurchasesResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _restorePurchasesTcs = restoreTcs;
                startRestore = true;
            }
        }

        if (startRestore)
        {
            try
            {
                _storeController.RestoreTransactions((success, error) =>
                {
                    if (!string.IsNullOrWhiteSpace(error))
                        Debug.LogWarning($"[SDK][IAP] Restore transactions result: success={success}, error={error}");
                    else
                        Debug.Log($"[SDK][IAP] Restore transactions result: success={success}");

                    if (!success)
                    {
                        CompleteRestorePurchases(
                            restoreTcs,
                            new RestorePurchasesResult
                            {
                                Success = false,
                                ErrorMessage = string.IsNullOrWhiteSpace(error)
                                    ? "Store restore did not complete successfully."
                                    : error
                            });
                        return;
                    }

                    try
                    {
                        _storeController.FetchPurchases();
                    }
                    catch (Exception ex)
                    {
                        CompleteRestorePurchases(
                            restoreTcs,
                            new RestorePurchasesResult
                            {
                                Success = false,
                                ErrorMessage = $"FetchPurchases after restore failed: {ex.Message}"
                            });
                    }
                });
            }
            catch (Exception ex)
            {
                CompleteRestorePurchases(
                    restoreTcs,
                    new RestorePurchasesResult
                    {
                        Success = false,
                        ErrorMessage = $"RestorePurchasesAsync failed to start: {ex.Message}"
                    });
            }
        }

        Task timeoutTask = Task.Delay(30000);
        Task cancelTask = null;
        CancellationTokenRegistration cancellationRegistration = default;
        if (cancellationToken.CanBeCanceled)
        {
            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationRegistration = cancellationToken.Register(() => cancelTcs.TrySetResult(true));
            cancelTask = cancelTcs.Task;
        }

        try
        {
            Task completed = cancelTask != null
                ? await Task.WhenAny(restoreTcs.Task, timeoutTask, cancelTask)
                : await Task.WhenAny(restoreTcs.Task, timeoutTask);

            if (completed == cancelTask)
                cancellationToken.ThrowIfCancellationRequested();

            if (completed == timeoutTask)
            {
                CompleteRestorePurchases(
                    restoreTcs,
                    new RestorePurchasesResult
                    {
                        Success = false,
                        ErrorMessage = "Restore purchases timed out."
                    });
            }

            return await restoreTcs.Task;
        }
        finally
        {
            cancellationRegistration.Dispose();
            lock (_pendingGate)
            {
                if (ReferenceEquals(_restorePurchasesTcs, restoreTcs) && restoreTcs.Task.IsCompleted)
                    _restorePurchasesTcs = null;
            }
        }
    }

    public bool HasEntitlement(string entitlementId) =>
        _entitlements.Has(entitlementId);

    public bool TryGetProductInfo(string productId, out RealMoneyProductInfo info)
    {
        info = null;
        if (!_isInitialized || _storeController == null || string.IsNullOrWhiteSpace(productId))
            return false;

        if (!_productsById.TryGetValue(productId, out RealMoneyProductDefinition definition))
            return false;

        Product product = FindStoreProduct(definition.ResolvedStoreProductId);
        if (product?.metadata == null)
            return false;

        ProductMetadata metadata = product.metadata;
        if (string.IsNullOrWhiteSpace(metadata.localizedPriceString))
            return false;

        info = new RealMoneyProductInfo
        {
            ProductId = productId,
            LocalizedPriceString = metadata.localizedPriceString,
            LocalizedTitle = metadata.localizedTitle ?? string.Empty,
            LocalizedDescription = metadata.localizedDescription ?? string.Empty,
            IsoCurrencyCode = metadata.isoCurrencyCode ?? string.Empty,
            LocalizedPrice = metadata.localizedPrice,
        };
        return true;
    }

    void FetchProducts()
    {
        if (_fetchRequested)
            return;

        _fetchRequested = true;
        _storeController.FetchProducts(
            _productsById.Values
                .Select(product => new ProductDefinition(product.ResolvedStoreProductId, product.ProductType))
                .ToList());
    }

    void OnProductsFetched(List<Product> products)
    {
        _areProductsReady = true;
        int count = products?.Count ?? 0;
        Debug.Log($"[SDK][IAP] Products fetched: {count}.");
        ProductsUpdated?.Invoke();
    }

    void OnProductsFetchFailed(ProductFetchFailed failure)
    {
        // Let EnsureProductsFetched / next purchase attempt request again.
        _fetchRequested = false;
        Debug.LogWarning(
            $"[SDK][IAP] Products fetch failed: {failure?.FailureReason}; " +
            $"failedCount={failure?.FailedFetchProducts?.Count ?? 0}");
    }

    void OnPurchasePending(PendingOrder order)
    {
        _ = HandlePurchasePendingAsync(order);
    }

    async Task HandlePurchasePendingAsync(PendingOrder order)
    {
        Interlocked.Increment(ref _pendingHandlersInFlight);
        try
        {
            await HandlePurchasePendingCoreAsync(order);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingHandlersInFlight);
        }
    }

    async Task HandlePurchasePendingCoreAsync(PendingOrder order)
    {
        Product product = order?.CartOrdered?.Items().FirstOrDefault()?.Product;
        string storeId = product?.definition?.id;
        if (product == null || string.IsNullOrWhiteSpace(storeId))
        {
            Debug.LogWarning("[SDK][IAP] Pending order has no product; cannot process.");
            return;
        }

        if (!TryResolveDefinition(storeId, out RealMoneyProductDefinition definition))
        {
            Debug.LogWarning($"[SDK][IAP] Pending order contains unknown store product '{storeId}'.");
            return;
        }

        string txKey = BuildTransactionDedupeKey(order, storeId);
        lock (_pendingGate)
        {
            if (!_txInFlightOrDone.Add(txKey))
            {
                Debug.Log($"[SDK][IAP] Skipping duplicate pending tx '{txKey}' for '{definition.ProductId}'.");
                return;
            }
        }

        string productId = definition.ProductId;
        bool granted = false;
        bool keepDedupe = false;
        try
        {
            if (definition.RedeemWithEconomy)
            {
                EconomyRedeemOutcome redeemOutcome =
                    await RedeemEconomyPurchaseAsync(order, product, definition);

                if (redeemOutcome == EconomyRedeemOutcome.Failed
                    || redeemOutcome == EconomyRedeemOutcome.Indeterminate)
                {
                    CompletePurchaseRequest(productId, false);
                    return;
                }

                if (redeemOutcome == EconomyRedeemOutcome.RedeemedByOtherPlayer)
                {
                    // Receipt already owned by another UGS player (e.g. after anonymous delete).
                    // Confirm the store order so Apple/Google stop redelivering it; do not claim grant.
                    keepDedupe = true;
                    TryConfirmPurchase(order, productId, afterGrant: false);
                    CompletePurchaseRequest(productId, false);
                    return;
                }

                // Success or AlreadyRedeemed (idempotent) — continue grant/confirm path.
                granted = true;
            }

            _entitlements.GrantRange(definition.GrantedEntitlementIds);
            granted = true;
            keepDedupe = true;
            MarkRewardsGranted(productId);

            // Complete success before store confirm so a sync OnPurchaseFailed from Confirm
            // cannot flip the awaiter to false after Economy/entitlements already applied.
            CompletePurchaseRequest(productId, true);
            PurchaseSucceeded?.Invoke(productId);

            TryConfirmPurchase(order, productId, afterGrant: true);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SDK][IAP] Failed to process purchase '{productId}': {ex}");
            if (granted)
            {
                keepDedupe = true;
                MarkRewardsGranted(productId);
                CompletePurchaseRequest(productId, true);
                PurchaseSucceeded?.Invoke(productId);
            }
            else
            {
                CompletePurchaseRequest(productId, false);
            }
        }
        finally
        {
            if (!keepDedupe)
            {
                lock (_pendingGate)
                    _txInFlightOrDone.Remove(txKey);
            }
        }
    }

    void TryConfirmPurchase(PendingOrder order, string productId, bool afterGrant)
    {
        try
        {
            _storeController.ConfirmPurchase(order);
        }
        catch (Exception confirmEx)
        {
            if (afterGrant)
            {
                Debug.LogWarning(
                    $"[SDK][IAP] Store confirm failed after grant for '{productId}': {confirmEx.Message}. " +
                    "Purchase already reported as success; store may retry confirm later.");
            }
            else
            {
                Debug.LogWarning(
                    $"[SDK][IAP] Store confirm failed for '{productId}' (no grant claimed): {confirmEx.Message}");
            }
        }
    }

    enum EconomyRedeemOutcome
    {
        Success,
        /// <summary>Economy already applied this receipt for the current player — treat as success + confirm.</summary>
        AlreadyRedeemed,
        /// <summary>Receipt was redeemed by a different UGS player — confirm store only.</summary>
        RedeemedByOtherPlayer,
        Failed,
        Indeterminate,
    }

    void MarkRewardsGranted(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return;

        if (string.Equals(_activePurchaseProductId, productId, StringComparison.Ordinal)
            || _purchaseRequests.ContainsKey(productId))
        {
            _lastPurchaseGrantedRewards = true;
        }
    }

    static string BuildTransactionDedupeKey(PendingOrder order, string storeId)
    {
        string tx = order?.Info?.TransactionID;
        if (!string.IsNullOrWhiteSpace(tx))
            return tx;

        // Fallback when store omits TransactionID — better than processing forever.
        string receipt = order?.Info?.Receipt;
        if (!string.IsNullOrWhiteSpace(receipt))
            return $"receipt:{storeId}:{receipt.GetHashCode():X8}";

        return $"pending:{storeId}:{Guid.NewGuid():N}";
    }

    async Task<EconomyRedeemOutcome> RedeemEconomyPurchaseAsync(
        PendingOrder order,
        Product product,
        RealMoneyProductDefinition definition)
    {
        string economyPurchaseId = definition.ProductId;
        string storeId = definition.ResolvedStoreProductId;

        if (!TryDetectStore(order, product, out string storeName))
        {
            Debug.LogWarning(
                $"[SDK][IAP] Cannot detect store for '{economyPurchaseId}' (store id '{storeId}'); " +
                "refusing to redeem.");
            return EconomyRedeemOutcome.Failed;
        }

        if (string.Equals(storeName, GooglePlay.Name, StringComparison.Ordinal))
            return await RedeemGooglePlayPurchaseAsync(order, product, definition);

        if (string.Equals(storeName, AppleAppStore.Name, StringComparison.Ordinal))
            return await RedeemAppleAppStorePurchaseAsync(order, product, definition);

        Debug.LogWarning(
            $"[SDK][IAP] Unsupported store '{storeName}' for '{economyPurchaseId}'; refusing to redeem.");
        return EconomyRedeemOutcome.Failed;
    }

    async Task<EconomyRedeemOutcome> RedeemGooglePlayPurchaseAsync(
        PendingOrder order,
        Product product,
        RealMoneyProductDefinition definition)
    {
        const int RedeemTimeoutMs = 15000;
        string economyPurchaseId = definition.ProductId;
        string storeId = definition.ResolvedStoreProductId;

        if (!TryResolveGoogleReceipt(order, product, out GoogleReceiptPayload googleReceipt))
        {
            Debug.LogWarning(
                $"[SDK][IAP] Google Play receipt missing/invalid for '{economyPurchaseId}' " +
                $"(store id '{storeId}'). " +
                $"product.receipt empty={string.IsNullOrWhiteSpace(product?.receipt)}; " +
                $"order.tx={order?.Info?.TransactionID}.");
            _lastRedeemIndeterminate = true;
            return EconomyRedeemOutcome.Indeterminate;
        }

        int localCost = ToMinorUnits(product);
        string localCurrency = product.metadata?.isoCurrencyCode ?? string.Empty;

        try
        {
            var args = new RedeemGooglePlayStorePurchaseArgs(
                economyPurchaseId,
                googleReceipt.json,
                googleReceipt.signature,
                localCost,
                localCurrency);
            await NetworkRequest.WithTimeout(
                EconomyService.Instance.Purchases.RedeemGooglePlayPurchaseAsync(args),
                timeoutMs: RedeemTimeoutMs);

            return await FinishSuccessfulRedeemAsync(economyPurchaseId);
        }
        catch (Exception ex)
        {
            return await HandleRedeemFailureAsync(economyPurchaseId, ex);
        }
    }

    async Task<EconomyRedeemOutcome> RedeemAppleAppStorePurchaseAsync(
        PendingOrder order,
        Product product,
        RealMoneyProductDefinition definition)
    {
        const int RedeemTimeoutMs = 15000;
        string economyPurchaseId = definition.ProductId;
        string storeId = definition.ResolvedStoreProductId;

        if (!TryResolveAppleReceipt(order, product, out string payload) ||
            string.IsNullOrWhiteSpace(payload))
        {
            payload = await ResolveAppleReceiptWithRetryAsync(order, product, economyPurchaseId, storeId);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            string jws = order?.Info?.Apple?.jwsRepresentation;
            Debug.LogWarning(
                $"[SDK][IAP] Apple App Receipt missing for '{economyPurchaseId}' (store id '{storeId}'). " +
                $"product.receipt empty={string.IsNullOrWhiteSpace(product?.receipt)}; " +
                $"order.tx={order?.Info?.TransactionID}; " +
                $"jwsPresent={!string.IsNullOrWhiteSpace(jws)}. " +
                "Economy RedeemAppleAppStorePurchaseAsync requires StoreKit 1 App Receipt, not jwsRepresentation.");
            _lastRedeemIndeterminate = true;
            return EconomyRedeemOutcome.Indeterminate;
        }

        int localCost = ToMinorUnits(product);
        string localCurrency = product.metadata?.isoCurrencyCode ?? string.Empty;

        try
        {
            var args = new RedeemAppleAppStorePurchaseArgs(
                economyPurchaseId,
                payload,
                localCost,
                localCurrency);
            await NetworkRequest.WithTimeout(
                EconomyService.Instance.Purchases.RedeemAppleAppStorePurchaseAsync(args),
                timeoutMs: RedeemTimeoutMs);

            return await FinishSuccessfulRedeemAsync(economyPurchaseId);
        }
        catch (Exception ex)
        {
            return await HandleRedeemFailureAsync(economyPurchaseId, ex);
        }
    }

    async Task<EconomyRedeemOutcome> FinishSuccessfulRedeemAsync(string economyPurchaseId)
    {
        NetworkStatus.ReportSuccess();

        if (_economy != null)
            await _economy.RefreshBalancesAsync();

        Debug.Log($"[SDK][IAP] Economy redeem succeeded for '{economyPurchaseId}'.");
        return EconomyRedeemOutcome.Success;
    }

    async Task<EconomyRedeemOutcome> HandleRedeemFailureAsync(string economyPurchaseId, Exception ex)
    {
        if (TryClassifyAlreadyRedeemed(ex, out bool otherPlayer))
        {
            if (otherPlayer)
            {
                Debug.LogWarning(
                    $"[SDK][IAP] Economy redeem for '{economyPurchaseId}' belongs to another player — " +
                    "confirming store order without granting on this account.");
                return EconomyRedeemOutcome.RedeemedByOtherPlayer;
            }

            Debug.LogWarning(
                $"[SDK][IAP] Economy redeem for '{economyPurchaseId}' already applied — " +
                "treating as success and confirming store order.");
            if (_economy != null)
            {
                try
                {
                    await _economy.RefreshBalancesAsync();
                }
                catch (Exception refreshEx)
                {
                    Debug.LogWarning(
                        $"[SDK][IAP] Balance refresh after already-redeemed failed: {refreshEx.Message}");
                }
            }

            return EconomyRedeemOutcome.AlreadyRedeemed;
        }

        if (ex is TimeoutException)
        {
            NetworkStatus.ReportFailure();
            _lastRedeemIndeterminate = true;
            Debug.LogError($"[SDK][IAP] Economy redeem timed out for '{economyPurchaseId}': {ex.Message}");
            return EconomyRedeemOutcome.Indeterminate;
        }

        if (IsRedeemTransportFailure(ex))
        {
            NetworkStatus.ReportFailure();
            _lastRedeemIndeterminate = true;
            Debug.LogError($"[SDK][IAP] Economy redeem transport failure for '{economyPurchaseId}': {ex.Message}");
            return EconomyRedeemOutcome.Indeterminate;
        }

        if (ex is EconomyException)
        {
            // Hard reject from Economy — typically not charged / not granted.
            Debug.LogError($"[SDK][IAP] Economy redeem failed for '{economyPurchaseId}': {ex}");
            return EconomyRedeemOutcome.Failed;
        }

        _lastRedeemIndeterminate = true;
        Debug.LogError($"[SDK][IAP] Unexpected redeem failure for '{economyPurchaseId}': {ex}");
        return EconomyRedeemOutcome.Indeterminate;
    }

    static bool TryClassifyAlreadyRedeemed(Exception ex, out bool otherPlayer)
    {
        otherPlayer = false;

        if (ex is EconomyAppleAppStorePurchaseFailedException apple)
        {
            AppleVerification.StatusOptions? status = apple.Data?.Verification?.Status;
            if (status == AppleVerification.StatusOptions.INVALIDALREADYREDEEMED)
                return true;
            if (status == AppleVerification.StatusOptions.INVALIDANOTHERPLAYER)
            {
                otherPlayer = true;
                return true;
            }
        }

        if (ex is EconomyGooglePlayStorePurchaseFailedException google)
        {
            GoogleVerification.StatusOptions? status = google.Data?.Verification?.Status;
            if (status == GoogleVerification.StatusOptions.INVALIDALREADYREDEEMED)
                return true;
            if (status == GoogleVerification.StatusOptions.INVALIDANOTHERPLAYER)
            {
                otherPlayer = true;
                return true;
            }
        }

        // Fallback when status payload is missing but Economy detail is clear.
        string message = ex?.Message ?? string.Empty;
        if (message.IndexOf("already been redeemed", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (message.IndexOf("another player", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            otherPlayer = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Detect store from order info / unified receipt / runtime platform.
    /// Never defaults one store to the other.
    /// </summary>
    static bool TryDetectStore(PendingOrder order, Product product, out string storeName)
    {
        storeName = null;

        if (order?.Info?.Google != null)
        {
            storeName = GooglePlay.Name;
            return true;
        }

        if (order?.Info?.Apple != null)
        {
            storeName = AppleAppStore.Name;
            return true;
        }

        if (TryExtractUnifiedPayload(order?.Info?.Receipt, out string fromOrder, out _) &&
            !string.IsNullOrWhiteSpace(fromOrder))
        {
            storeName = fromOrder;
            return true;
        }

        if (TryExtractUnifiedPayload(product?.receipt, out string fromProduct, out _) &&
            !string.IsNullOrWhiteSpace(fromProduct))
        {
            storeName = fromProduct;
            return true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        storeName = GooglePlay.Name;
        return true;
#elif UNITY_IOS && !UNITY_EDITOR
        storeName = AppleAppStore.Name;
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// Google Play only: unified receipt Payload → { json, signature }.
    /// No Apple App Receipt / RefreshAppReceipt / jws paths.
    /// </summary>
    static bool TryResolveGoogleReceipt(
        PendingOrder order,
        Product product,
        out GoogleReceiptPayload googleReceipt)
    {
        googleReceipt = null;

        if (TryExtractUnifiedPayload(order?.Info?.Receipt, out string storeName, out string payload) &&
            IsGoogleStore(storeName) &&
            TryParseGoogleReceiptPayload(payload, out googleReceipt))
        {
            return true;
        }

        if (TryExtractUnifiedPayload(product?.receipt, out storeName, out payload) &&
            IsGoogleStore(storeName) &&
            TryParseGoogleReceiptPayload(payload, out googleReceipt))
        {
            return true;
        }

        return false;
    }

    static bool TryParseGoogleReceiptPayload(string payload, out GoogleReceiptPayload googleReceipt)
    {
        googleReceipt = null;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        GoogleReceiptPayload parsed = JsonUtility.FromJson<GoogleReceiptPayload>(payload);
        if (parsed == null ||
            string.IsNullOrWhiteSpace(parsed.json) ||
            string.IsNullOrWhiteSpace(parsed.signature))
        {
            return false;
        }

        googleReceipt = parsed;
        return true;
    }

    static bool IsGoogleStore(string storeName) =>
        string.Equals(storeName, GooglePlay.Name, StringComparison.Ordinal);

    /// <summary>
    /// Apple only: App Receipt from order / Apple extended service / unified Payload.
    /// </summary>
    bool TryResolveAppleReceipt(PendingOrder order, Product product, out string payload)
    {
        payload = null;

        // Prefer order info: Product.receipt on Apple returns null when transactionID is unset.
        string appReceipt = order?.Info?.Apple?.AppReceipt;
        if (!string.IsNullOrWhiteSpace(appReceipt))
        {
            payload = appReceipt;
            return true;
        }

        if (TryExtractUnifiedPayload(order?.Info?.Receipt, out string storeName, out string unifiedPayload) &&
            IsAppleStore(storeName) &&
            !string.IsNullOrWhiteSpace(unifiedPayload))
        {
            payload = unifiedPayload;
            return true;
        }

        string serviceReceipt = _storeController?.AppleStoreExtendedPurchaseService?.appReceipt;
        if (!string.IsNullOrWhiteSpace(serviceReceipt))
        {
            payload = serviceReceipt;
            return true;
        }

        if (TryExtractUnifiedPayload(product?.receipt, out storeName, out unifiedPayload) &&
            IsAppleStore(storeName) &&
            !string.IsNullOrWhiteSpace(unifiedPayload))
        {
            payload = unifiedPayload;
            return true;
        }

        return false;
    }

    static bool IsAppleStore(string storeName) =>
        string.Equals(storeName, AppleAppStore.Name, StringComparison.Ordinal);

    /// <summary>
    /// IAP 5 + StoreKit 2: App Receipt often lags until Unity's post-purchase refresh finishes.
    /// Poll + RefreshAppReceipt — Apple path only.
    /// </summary>
    async Task<string> ResolveAppleReceiptWithRetryAsync(
        PendingOrder order,
        Product product,
        string economyPurchaseId,
        string storeId)
    {
        const int pollAttempts = 6;
        const int pollDelayMs = 250;

        for (int i = 0; i < pollAttempts; i++)
        {
            await Task.Delay(pollDelayMs);
            if (TryResolveAppleReceipt(order, product, out string payload) &&
                !string.IsNullOrWhiteSpace(payload))
            {
                Debug.Log(
                    $"[SDK][IAP] Apple App Receipt became available after poll #{i + 1} for '{economyPurchaseId}'.");
                return payload;
            }
        }

        IAppleStoreExtendedPurchaseService apple = _storeController?.AppleStoreExtendedPurchaseService;
        if (apple == null)
            return null;

        Debug.Log($"[SDK][IAP] Refreshing Apple App Receipt for '{economyPurchaseId}' (store id '{storeId}')...");
        try
        {
            string refreshed = await RefreshAppleAppReceiptAsync(apple);
            if (!string.IsNullOrWhiteSpace(refreshed))
                return refreshed;

            if (TryResolveAppleReceipt(order, product, out string afterRefresh) &&
                !string.IsNullOrWhiteSpace(afterRefresh))
                return afterRefresh;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SDK][IAP] RefreshAppReceipt failed for '{economyPurchaseId}': {ex.Message}");
        }

        return null;
    }

    static bool TryExtractUnifiedPayload(string unifiedReceiptJson, out string storeName, out string payload)
    {
        storeName = null;
        payload = null;
        if (string.IsNullOrWhiteSpace(unifiedReceiptJson))
            return false;

        UnifiedReceipt unified = JsonUtility.FromJson<UnifiedReceipt>(unifiedReceiptJson);
        if (unified == null || string.IsNullOrWhiteSpace(unified.Payload))
            return false;

        storeName = unified.Store;
        payload = unified.Payload;
        return true;
    }

    static async Task<string> RefreshAppleAppReceiptAsync(IAppleStoreExtendedPurchaseService apple)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            apple.RefreshAppReceipt(
                receipt => tcs.TrySetResult(receipt),
                error => tcs.TrySetException(
                    new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error) ? "RefreshAppReceipt failed." : error)));
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            return await tcs.Task;
        }

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(15000));
        if (completed != tcs.Task)
            throw new TimeoutException("RefreshAppReceipt timed out after 15s.");

        return await tcs.Task;
    }

    void OnPurchaseConfirmed(Order order)
    {
        Product product = order?.CartOrdered?.Items().FirstOrDefault()?.Product;
        if (product != null)
            Debug.Log($"[SDK][IAP] Purchase confirmed: {product.definition.id}");
    }

    void OnPurchaseFailed(FailedOrder order)
    {
        Product product = order?.CartOrdered?.Items().FirstOrDefault()?.Product;
        string storeId = product?.definition?.id ?? "unknown";
        string productId = TryResolveDefinition(storeId, out RealMoneyProductDefinition definition)
            ? definition.ProductId
            : storeId;
        bool userCancelled = order != null
            && order.FailureReason == PurchaseFailureReason.UserCancelled;

        // Only attribute cancel/failure to LastPurchaseWasUserCancelled when this failure
        // still owns an in-flight PurchaseAsync (ignore late callbacks after token cancel).
        bool hadRequest = !string.IsNullOrWhiteSpace(productId)
            && _purchaseRequests.ContainsKey(productId);
        if (hadRequest)
            _lastPurchaseWasUserCancelled = userCancelled;

        if (userCancelled)
            Debug.Log($"[SDK][IAP] Purchase cancelled by user: {productId}; storeId={storeId}");
        else
            Debug.LogWarning(
                $"[SDK][IAP] Purchase failed: {productId}; storeId={storeId}; " +
                $"reason={order?.FailureReason}; details={order?.Details}");

        // If rewards already landed from a parallel pending redeem, keep success path.
        if (_lastPurchaseGrantedRewards
            && string.Equals(productId, _activePurchaseProductId, StringComparison.Ordinal))
        {
            CompletePurchaseRequest(productId, true);
            return;
        }

        CompletePurchaseRequest(productId, false);
    }

    void OnPurchasesFetched(Orders orders)
    {
        var restoredProductIds = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (orders == null)
                return;

            int pendingCount = orders.PendingOrders?.Count ?? 0;
            if (pendingCount > 0)
            {
                Debug.Log($"[SDK][IAP] FetchPurchases returned {pendingCount} pending order(s) — redeeming.");
                foreach (PendingOrder pending in orders.PendingOrders)
                    _ = HandlePurchasePendingAsync(pending);
            }

            foreach (RealMoneyProductDefinition definition in _productsById.Values)
            {
                if (!definition.RestoreEntitlementsFromExistingPurchases)
                    continue;

                string storeId = definition.ResolvedStoreProductId;
                bool foundExistingPurchase =
                    orders.ConfirmedOrders.Any(order => ContainsProduct(order, storeId));

                if (!foundExistingPurchase)
                    continue;

                restoredProductIds.Add(definition.ProductId);
                _entitlements.GrantRange(definition.GrantedEntitlementIds);
                if (definition.GrantedEntitlementIds != null && definition.GrantedEntitlementIds.Length > 0)
                    Debug.Log($"[SDK][IAP] Restored entitlements for '{definition.ProductId}'.");
            }
        }
        finally
        {
            TaskCompletionSource<bool> tcs;
            TaskCompletionSource<RestorePurchasesResult> restoreTcs;
            lock (_pendingGate)
            {
                tcs = _purchasesFetchTcs;
                restoreTcs = _restorePurchasesTcs;
            }
            tcs?.TrySetResult(true);
            if (restoreTcs != null && !restoreTcs.Task.IsCompleted)
            {
                CompleteRestorePurchases(
                    restoreTcs,
                    new RestorePurchasesResult
                    {
                        Success = true,
                        RestoredAnyEntitlements = restoredProductIds.Count > 0,
                        RestoredProductIds = restoredProductIds.ToArray()
                    });
            }
        }
    }

    void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
    {
        Debug.LogWarning($"[SDK][IAP] Existing purchases fetch failed: {failure?.Message}");
        TaskCompletionSource<bool> tcs;
        TaskCompletionSource<RestorePurchasesResult> restoreTcs;
        lock (_pendingGate)
        {
            tcs = _purchasesFetchTcs;
            restoreTcs = _restorePurchasesTcs;
        }
        tcs?.TrySetResult(false);
        if (restoreTcs != null && !restoreTcs.Task.IsCompleted)
        {
            CompleteRestorePurchases(
                restoreTcs,
                new RestorePurchasesResult
                {
                    Success = false,
                    ErrorMessage = string.IsNullOrWhiteSpace(failure?.Message)
                        ? "Existing purchases fetch failed."
                        : failure.Message
                });
        }
    }

    void CompleteRestorePurchases(
        TaskCompletionSource<RestorePurchasesResult> restoreTcs,
        RestorePurchasesResult result)
    {
        restoreTcs?.TrySetResult(result ?? new RestorePurchasesResult
        {
            Success = false,
            ErrorMessage = "Restore purchases completed without a result."
        });
    }

    Product FindStoreProduct(string storeId) =>
        _storeController
            .GetProducts()
            .FirstOrDefault(candidate => candidate.definition.id == storeId);

    bool TryResolveDefinition(string storeOrEconomyId, out RealMoneyProductDefinition definition)
    {
        if (_productsByStoreId.TryGetValue(storeOrEconomyId, out definition))
            return true;
        return _productsById.TryGetValue(storeOrEconomyId, out definition);
    }

    void CompletePurchaseRequest(string productId, bool success)
    {
        if (string.IsNullOrWhiteSpace(productId))
            return;

        if (_purchaseRequests.TryGetValue(productId, out TaskCompletionSource<bool> tcs))
        {
            _purchaseRequests.Remove(productId);
            tcs.TrySetResult(success);
        }
    }

    static bool ContainsProduct(Order order, string storeProductId) =>
        order?.CartOrdered?.Items().Any(item => item.Product?.definition?.id == storeProductId) == true;

    static int ToMinorUnits(Product product)
    {
        decimal localizedPrice = product?.metadata?.localizedPrice ?? 0m;
        string currencyCode = product?.metadata?.isoCurrencyCode;
        int fractionDigits = GetIso4217FractionDigits(currencyCode);
        decimal scale = 1m;
        for (int i = 0; i < fractionDigits; i++)
            scale *= 10m;

        long minor = (long)decimal.Round(localizedPrice * scale, MidpointRounding.AwayFromZero);
        if (minor > int.MaxValue)
            return int.MaxValue;
        if (minor < int.MinValue)
            return int.MinValue;
        return (int)minor;
    }

    static bool IsRedeemTransportFailure(Exception exception)
    {
        for (Exception walk = exception; walk != null; walk = walk.InnerException)
        {
            if (walk is SocketException)
                return true;
            if (walk is HttpRequestException)
                return true;
        }

        return false;
    }

    /// <summary>ISO 4217 minor-unit exponent (0 / 2 / 3). Unknown codes default to 2.</summary>
    static int GetIso4217FractionDigits(string isoCurrencyCode)
    {
        if (string.IsNullOrWhiteSpace(isoCurrencyCode))
            return 2;

        switch (isoCurrencyCode.Trim().ToUpperInvariant())
        {
            // Zero-decimal
            case "BIF":
            case "CLP":
            case "DJF":
            case "GNF":
            case "ISK":
            case "JPY":
            case "KMF":
            case "KRW":
            case "PYG":
            case "RWF":
            case "UGX":
            case "UYI":
            case "VND":
            case "VUV":
            case "XAF":
            case "XOF":
            case "XPF":
                return 0;
            // Three-decimal
            case "BHD":
            case "IQD":
            case "JOD":
            case "KWD":
            case "LYD":
            case "OMR":
            case "TND":
                return 3;
            default:
                return 2;
        }
    }
}
