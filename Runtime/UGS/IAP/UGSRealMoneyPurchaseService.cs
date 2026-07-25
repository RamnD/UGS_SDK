using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
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

    StoreController _storeController;
    bool _isInitialized;
    bool _fetchRequested;
    bool _areProductsReady;
    bool _lastPurchaseWasUserCancelled;

    public bool IsInitialized => _isInitialized;

    public bool AreProductsReady => _areProductsReady;

    public bool LastPurchaseWasUserCancelled => _lastPurchaseWasUserCancelled;

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
        _storeController.FetchPurchases();
        _isInitialized = true;
        Debug.Log("[SDK][IAP] Store connected.");
    }

    public void EnsureProductsFetched()
    {
        if (!_isInitialized || _storeController == null || _areProductsReady)
            return;

        // Allow a fresh request after failure or if the first FetchProducts never completed.
        _fetchRequested = false;
        FetchProducts();
    }

    public async Task<bool> PurchaseAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        _lastPurchaseWasUserCancelled = false;

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
            return false;
        }

        string storeId = definition.ResolvedStoreProductId;
        Product product = FindStoreProduct(storeId);
        if (product == null)
        {
            EnsureProductsFetched();
            Debug.LogWarning(
                $"[SDK][IAP] Product '{productId}' (store id '{storeId}') not fetched from the store — refetch kicked.");
            return false;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _purchaseRequests[productId] = tcs;
        _storeController.PurchaseProduct(product);

        using CancellationTokenRegistration ctr = cancellationToken.Register(() =>
        {
            if (_purchaseRequests.Remove(productId))
                tcs.TrySetCanceled(cancellationToken);
        });

        return await tcs.Task;
    }

    public void RestorePurchases()
    {
        if (_storeController == null)
            throw new InvalidOperationException("InitializeAsync must complete before RestorePurchases.");

        _storeController.RestoreTransactions((success, error) =>
        {
            if (!string.IsNullOrWhiteSpace(error))
                Debug.LogWarning($"[SDK][IAP] Restore transactions result: success={success}, error={error}");
            else
                Debug.Log($"[SDK][IAP] Restore transactions result: success={success}");
        });
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

        string productId = definition.ProductId;
        bool granted = false;
        try
        {
            if (definition.RedeemWithEconomy)
            {
                bool redeemed = await RedeemEconomyPurchaseAsync(order, product, definition);
                if (!redeemed)
                {
                    CompletePurchaseRequest(productId, false);
                    return;
                }

                granted = true;
            }

            _entitlements.GrantRange(definition.GrantedEntitlementIds);
            granted = true;

            // Complete success before store confirm so a sync OnPurchaseFailed from Confirm
            // cannot flip the awaiter to false after Economy/entitlements already applied.
            CompletePurchaseRequest(productId, true);
            PurchaseSucceeded?.Invoke(productId);

            try
            {
                _storeController.ConfirmPurchase(order);
            }
            catch (Exception confirmEx)
            {
                Debug.LogWarning(
                    $"[SDK][IAP] Store confirm failed after grant for '{productId}': {confirmEx.Message}. " +
                    "Purchase already reported as success; store may retry confirm later.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SDK][IAP] Failed to process purchase '{productId}': {ex}");
            if (granted)
            {
                CompletePurchaseRequest(productId, true);
                PurchaseSucceeded?.Invoke(productId);
            }
            else
            {
                CompletePurchaseRequest(productId, false);
            }
        }
    }

    async Task<bool> RedeemEconomyPurchaseAsync(
        PendingOrder order,
        Product product,
        RealMoneyProductDefinition definition)
    {
        const int RedeemTimeoutMs = 15000;
        string economyPurchaseId = definition.ProductId;
        string storeId = definition.ResolvedStoreProductId;

        if (!TryResolveRedeemReceipt(order, product, out string storeName, out string payload) ||
            string.IsNullOrWhiteSpace(payload))
        {
            string recovered = await ResolveReceiptPayloadWithRetryAsync(
                order, product, economyPurchaseId, storeId);
            if (!string.IsNullOrWhiteSpace(recovered))
                payload = recovered;

            // Prefer structured resolve after refresh/poll so Google vs Apple store name is correct.
            if (TryResolveRedeemReceipt(order, product, out string resolvedStore, out string resolvedPayload) &&
                !string.IsNullOrWhiteSpace(resolvedPayload))
            {
                storeName = resolvedStore;
                payload = resolvedPayload;
            }
            else if (string.IsNullOrWhiteSpace(storeName) && !string.IsNullOrWhiteSpace(payload))
            {
                storeName = AppleAppStore.Name;
            }
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            string jws = order?.Info?.Apple?.jwsRepresentation;
            Debug.LogWarning(
                $"[SDK][IAP] Product '{economyPurchaseId}' (store id '{storeId}') has no legacy App Receipt. " +
                $"product.receipt empty={string.IsNullOrWhiteSpace(product?.receipt)}; " +
                $"order.tx={order?.Info?.TransactionID}; " +
                $"jwsPresent={!string.IsNullOrWhiteSpace(jws)}. " +
                "Economy RedeemAppleAppStorePurchaseAsync requires StoreKit 1 App Receipt, not jwsRepresentation.");
            return false;
        }

        int localCost = ToMinorUnits(product);
        string localCurrency = product.metadata?.isoCurrencyCode ?? string.Empty;

        try
        {
            if (string.Equals(storeName, GooglePlay.Name, StringComparison.Ordinal))
            {
                GoogleReceiptPayload googleReceipt = JsonUtility.FromJson<GoogleReceiptPayload>(payload);
                if (googleReceipt == null ||
                    string.IsNullOrWhiteSpace(googleReceipt.json) ||
                    string.IsNullOrWhiteSpace(googleReceipt.signature))
                {
                    Debug.LogWarning($"[SDK][IAP] Invalid Google receipt payload for '{economyPurchaseId}'.");
                    return false;
                }

                var args = new RedeemGooglePlayStorePurchaseArgs(
                    economyPurchaseId,
                    googleReceipt.json,
                    googleReceipt.signature,
                    localCost,
                    localCurrency);
                await NetworkRequest.WithTimeout(
                    EconomyService.Instance.Purchases.RedeemGooglePlayPurchaseAsync(args),
                    timeoutMs: RedeemTimeoutMs);
            }
            else
            {
                var args = new RedeemAppleAppStorePurchaseArgs(
                    economyPurchaseId,
                    payload,
                    localCost,
                    localCurrency);
                await NetworkRequest.WithTimeout(
                    EconomyService.Instance.Purchases.RedeemAppleAppStorePurchaseAsync(args),
                    timeoutMs: RedeemTimeoutMs);
            }

            NetworkStatus.ReportSuccess();

            if (_economy != null)
                await _economy.RefreshBalancesAsync();

            Debug.Log($"[SDK][IAP] Economy redeem succeeded for '{economyPurchaseId}'.");
            return true;
        }
        catch (TimeoutException ex)
        {
            NetworkStatus.ReportFailure();
            Debug.LogError($"[SDK][IAP] Economy redeem timed out for '{economyPurchaseId}': {ex.Message}");
            return false;
        }
        catch (Exception ex) when (IsRedeemTransportFailure(ex))
        {
            NetworkStatus.ReportFailure();
            Debug.LogError($"[SDK][IAP] Economy redeem transport failure for '{economyPurchaseId}': {ex.Message}");
            return false;
        }
        catch (EconomyException ex)
        {
            Debug.LogError($"[SDK][IAP] Economy redeem failed for '{economyPurchaseId}': {ex}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SDK][IAP] Unexpected redeem failure for '{economyPurchaseId}': {ex}");
            return false;
        }
    }

    /// <summary>
    /// IAP 5 + StoreKit 2: <see cref="Product.receipt"/> is null when Product.transactionID is unset,
    /// and Apple App Receipt often lags until Unity's post-purchase refresh finishes.
    /// Prefer <see cref="IOrderInfo"/>, then poll / RefreshAppReceipt for Economy-compatible payload.
    /// </summary>
    async Task<string> ResolveReceiptPayloadWithRetryAsync(
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
            if (TryResolveRedeemReceipt(order, product, out _, out string payload) &&
                !string.IsNullOrWhiteSpace(payload))
            {
                Debug.Log(
                    $"[SDK][IAP] Legacy receipt became available after poll #{i + 1} for '{economyPurchaseId}'.");
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

            if (TryResolveRedeemReceipt(order, product, out _, out string afterRefresh) &&
                !string.IsNullOrWhiteSpace(afterRefresh))
                return afterRefresh;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SDK][IAP] RefreshAppReceipt failed for '{economyPurchaseId}': {ex.Message}");
        }

        return null;
    }

    bool TryResolveRedeemReceipt(
        PendingOrder order,
        Product product,
        out string storeName,
        out string payload)
    {
        storeName = null;
        payload = null;

        // Prefer order info: Product.receipt on Apple returns null when transactionID is unset.
        string appReceipt = order?.Info?.Apple?.AppReceipt;
        if (!string.IsNullOrWhiteSpace(appReceipt))
        {
            storeName = order.Info.Apple.StoreName;
            if (string.IsNullOrWhiteSpace(storeName))
                storeName = AppleAppStore.Name;
            payload = appReceipt;
            return true;
        }

        if (TryExtractUnifiedPayload(order?.Info?.Receipt, out storeName, out payload))
            return true;

        string serviceReceipt = _storeController?.AppleStoreExtendedPurchaseService?.appReceipt;
        if (!string.IsNullOrWhiteSpace(serviceReceipt))
        {
            storeName = AppleAppStore.Name;
            payload = serviceReceipt;
            return true;
        }

        return TryExtractUnifiedPayload(product?.receipt, out storeName, out payload);
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

        CompletePurchaseRequest(productId, false);
    }

    void OnPurchasesFetched(Orders orders)
    {
        if (orders == null)
            return;

        foreach (RealMoneyProductDefinition definition in _productsById.Values)
        {
            if (!definition.RestoreEntitlementsFromExistingPurchases)
                continue;

            string storeId = definition.ResolvedStoreProductId;
            bool foundExistingPurchase =
                orders.ConfirmedOrders.Any(order => ContainsProduct(order, storeId));

            if (!foundExistingPurchase)
                continue;

            _entitlements.GrantRange(definition.GrantedEntitlementIds);
            if (definition.GrantedEntitlementIds != null && definition.GrantedEntitlementIds.Length > 0)
                Debug.Log($"[SDK][IAP] Restored entitlements for '{definition.ProductId}'.");
        }
    }

    void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
    {
        Debug.LogWarning($"[SDK][IAP] Existing purchases fetch failed: {failure?.Message}");
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
