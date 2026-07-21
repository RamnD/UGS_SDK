using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;
using UnityEngine.Purchasing;

/// <summary>
/// Unity IAP + UGS Economy bridge for portable real-money purchases.
/// Uses the configured product id as the Economy real-money purchase id.
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

    StoreController _storeController;
    bool _isInitialized;
    bool _fetchRequested;

    public bool IsInitialized => _isInitialized;

    public event Action<string> PurchaseSucceeded;

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

            _productsById[product.ProductId] = product;
        }

        _storeController = UnityIAPServices.StoreController();
        _storeController.OnPurchasePending += OnPurchasePending;
        _storeController.OnPurchaseConfirmed += OnPurchaseConfirmed;
        _storeController.OnPurchaseFailed += OnPurchaseFailed;
        _storeController.OnPurchasesFetched += OnPurchasesFetched;
        _storeController.OnPurchasesFetchFailed += OnPurchasesFetchFailed;

        await _storeController.Connect();
        FetchProducts();
        _storeController.FetchPurchases();
        _isInitialized = true;
        Debug.Log("[SDK][IAP] Store connected.");
    }

    public async Task<bool> PurchaseAsync(
        string productId,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized || _storeController == null)
            throw new InvalidOperationException("InitializeAsync must complete before PurchaseAsync.");

        if (!_productsById.ContainsKey(productId))
            throw new InvalidOperationException($"Product '{productId}' is not registered in this purchase service.");

        if (_purchaseRequests.ContainsKey(productId))
        {
            Debug.LogWarning($"[SDK][IAP] Purchase already pending for '{productId}'.");
            return false;
        }

        Product product = _storeController
            .GetProducts()
            .FirstOrDefault(candidate => candidate.definition.id == productId);
        if (product == null)
        {
            Debug.LogWarning($"[SDK][IAP] Product '{productId}' not fetched from the store.");
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

    void FetchProducts()
    {
        if (_fetchRequested)
            return;

        _fetchRequested = true;
        _storeController.FetchProducts(
            _productsById.Values
                .Select(product => new ProductDefinition(product.ProductId, product.ProductType))
                .ToList());
    }

    void OnPurchasePending(PendingOrder order)
    {
        _ = HandlePurchasePendingAsync(order);
    }

    async Task HandlePurchasePendingAsync(PendingOrder order)
    {
        Product product = order?.CartOrdered?.Items().FirstOrDefault()?.Product;
        string productId = product?.definition?.id;
        if (product == null || string.IsNullOrWhiteSpace(productId))
        {
            Debug.LogWarning("[SDK][IAP] Pending order has no product; cannot process.");
            return;
        }

        if (!_productsById.TryGetValue(productId, out RealMoneyProductDefinition definition))
        {
            Debug.LogWarning($"[SDK][IAP] Pending order contains unknown product '{productId}'.");
            CompletePurchaseRequest(productId, false);
            return;
        }

        try
        {
            if (definition.RedeemWithEconomy)
            {
                bool redeemed = await RedeemEconomyPurchaseAsync(product);
                if (!redeemed)
                {
                    CompletePurchaseRequest(productId, false);
                    return;
                }
            }

            _entitlements.GrantRange(definition.GrantedEntitlementIds);
            _storeController.ConfirmPurchase(order);
            CompletePurchaseRequest(productId, true);
            PurchaseSucceeded?.Invoke(productId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SDK][IAP] Failed to process purchase '{productId}': {ex}");
            CompletePurchaseRequest(productId, false);
        }
    }

    async Task<bool> RedeemEconomyPurchaseAsync(Product product)
    {
        string productId = product.definition.id;
        string receipt = product.receipt;
        if (string.IsNullOrWhiteSpace(receipt))
        {
            Debug.LogWarning($"[SDK][IAP] Product '{productId}' has no receipt.");
            return false;
        }

        int localCost = ToMinorUnits(product);
        string localCurrency = product.metadata?.isoCurrencyCode ?? string.Empty;

        try
        {
            UnifiedReceipt unifiedReceipt = JsonUtility.FromJson<UnifiedReceipt>(receipt);
            if (unifiedReceipt == null || string.IsNullOrWhiteSpace(unifiedReceipt.Payload))
            {
                Debug.LogWarning($"[SDK][IAP] Unified receipt payload missing for '{productId}'.");
                return false;
            }

            if (string.Equals(unifiedReceipt.Store, GooglePlay.Name, StringComparison.Ordinal))
            {
                GoogleReceiptPayload googleReceipt = JsonUtility.FromJson<GoogleReceiptPayload>(unifiedReceipt.Payload);
                if (googleReceipt == null ||
                    string.IsNullOrWhiteSpace(googleReceipt.json) ||
                    string.IsNullOrWhiteSpace(googleReceipt.signature))
                {
                    Debug.LogWarning($"[SDK][IAP] Invalid Google receipt payload for '{productId}'.");
                    return false;
                }

                var args = new RedeemGooglePlayStorePurchaseArgs(
                    productId,
                    googleReceipt.json,
                    googleReceipt.signature,
                    localCost,
                    localCurrency);
                await EconomyService.Instance.Purchases.RedeemGooglePlayPurchaseAsync(args);
            }
            else
            {
                var args = new RedeemAppleAppStorePurchaseArgs(
                    productId,
                    unifiedReceipt.Payload,
                    localCost,
                    localCurrency);
                await EconomyService.Instance.Purchases.RedeemAppleAppStorePurchaseAsync(args);
            }

            if (_economy != null)
                await _economy.RefreshBalancesAsync();

            Debug.Log($"[SDK][IAP] Economy redeem succeeded for '{productId}'.");
            return true;
        }
        catch (EconomyException ex)
        {
            Debug.LogError($"[SDK][IAP] Economy redeem failed for '{productId}': {ex}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SDK][IAP] Unexpected redeem failure for '{productId}': {ex}");
            return false;
        }
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
        string productId = product?.definition?.id ?? "unknown";
        Debug.LogWarning(
            $"[SDK][IAP] Purchase failed: {productId}; reason={order?.FailureReason}; details={order?.Details}");
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

            bool foundExistingPurchase =
                orders.ConfirmedOrders.Any(order => ContainsProduct(order, definition.ProductId))
                || orders.PendingOrders.Any(order => ContainsProduct(order, definition.ProductId));

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

    static bool ContainsProduct(Order order, string productId) =>
        order?.CartOrdered?.Items().Any(item => item.Product?.definition?.id == productId) == true;

    static int ToMinorUnits(Product product)
    {
        decimal localizedPrice = product?.metadata?.localizedPrice ?? 0m;
        return (int)decimal.Round(localizedPrice * 100m, MidpointRounding.AwayFromZero);
    }
}
