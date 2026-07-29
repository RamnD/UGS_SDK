using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// <see cref="IVirtualPurchaseService"/> implementation via UGS Economy Virtual Purchases.
/// Lazy-syncs Economy configuration (shared with the purchase catalog), enforces single-flight
/// purchases, and optionally refreshes <typeparamref name="TCurrency"/> balances after success.
/// </summary>
/// <typeparam name="TCurrency">Game currency enum used by the optional inventory refresh.</typeparam>
public sealed class UGSVirtualPurchaseService<TCurrency> : IVirtualPurchaseService
    where TCurrency : struct, Enum
{
    const int PurchaseTimeoutMs = 15000;

    readonly object _purchaseGate = new();
    readonly IInventoryService<TCurrency> _economy;

    bool _purchaseInFlight;

    /// <inheritdoc/>
    public event Action<string> PurchaseSucceeded;

    /// <summary>
    /// Creates a virtual-purchase client.
    /// </summary>
    /// <param name="economy">
    /// Optional inventory service; when set, balances are refreshed after a successful purchase.
    /// </param>
    public UGSVirtualPurchaseService(IInventoryService<TCurrency> economy = null)
    {
        _economy = economy;
    }

    /// <inheritdoc/>
    public async Task<bool> PurchaseAsync(string purchaseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(purchaseId))
            throw new ArgumentException("Virtual purchase id must be non-empty.", nameof(purchaseId));

        lock (_purchaseGate)
        {
            if (_purchaseInFlight)
            {
                Debug.LogWarning(
                    $"[SDK][VirtualPurchase] Purchase rejected for '{purchaseId}' — another purchase is already in flight.");
                return false;
            }

            _purchaseInFlight = true;
        }

        try
        {
            return await PurchaseCoreAsync(purchaseId, cancellationToken);
        }
        finally
        {
            lock (_purchaseGate)
                _purchaseInFlight = false;
        }
    }

    async Task<bool> PurchaseCoreAsync(string purchaseId, CancellationToken cancellationToken)
    {
        if (!NetworkStatus.IsOnline)
        {
            Debug.LogWarning($"[SDK][VirtualPurchase] Purchase requires network: '{purchaseId}'.");
            return false;
        }

        await UGSEconomyConfigurationSync.SyncAsync(cancellationToken);

        try
        {
            await NetworkRequest.WithTimeout(
                EconomyService.Instance.Purchases.MakeVirtualPurchaseAsync(purchaseId),
                cancellationToken,
                timeoutMs: PurchaseTimeoutMs);

            await RefreshBalancesIfAvailableAsync(cancellationToken);
            NetworkStatus.ReportSuccess();
            PurchaseSucceeded?.Invoke(purchaseId);
            Debug.Log($"[SDK][VirtualPurchase] Purchase succeeded for '{purchaseId}'.");
            return true;
        }
        catch (EconomyException ex) when (ex.Reason == EconomyExceptionReason.ConfigNotSynced)
        {
            Debug.LogWarning(
                $"[SDK][VirtualPurchase] Config not synced for '{purchaseId}' — resyncing once.");
            UGSEconomyConfigurationSync.Invalidate();
            await UGSEconomyConfigurationSync.SyncAsync(cancellationToken, force: true);

            try
            {
                await NetworkRequest.WithTimeout(
                    EconomyService.Instance.Purchases.MakeVirtualPurchaseAsync(purchaseId),
                    cancellationToken,
                    timeoutMs: PurchaseTimeoutMs);

                await RefreshBalancesIfAvailableAsync(cancellationToken);
                NetworkStatus.ReportSuccess();
                PurchaseSucceeded?.Invoke(purchaseId);
                Debug.Log($"[SDK][VirtualPurchase] Purchase succeeded for '{purchaseId}' after config resync.");
                return true;
            }
            catch (Exception retryEx)
            {
                return HandlePurchaseFailure(purchaseId, retryEx);
            }
        }
        catch (Exception ex)
        {
            return HandlePurchaseFailure(purchaseId, ex);
        }
    }

    async Task RefreshBalancesIfAvailableAsync(CancellationToken cancellationToken)
    {
        if (_economy != null)
            await _economy.RefreshBalancesAsync(cancellationToken);
    }

    static bool HandlePurchaseFailure(string purchaseId, Exception ex)
    {
        if (ex is TimeoutException)
        {
            NetworkStatus.ReportFailure();
            Debug.LogError($"[SDK][VirtualPurchase] Purchase timed out for '{purchaseId}': {ex.Message}");
            return false;
        }

        if (EconomyErrorClassifier.IsRecoverable(ex) || EconomyErrorClassifier.IsIndeterminate(ex))
        {
            NetworkStatus.ReportFailure();
            Debug.LogError($"[SDK][VirtualPurchase] Transport failure for '{purchaseId}': {ex.Message}");
            return false;
        }

        if (ex is EconomyException)
        {
            Debug.LogError($"[SDK][VirtualPurchase] Purchase failed for '{purchaseId}': {ex}");
            return false;
        }

        Debug.LogError($"[SDK][VirtualPurchase] Unexpected purchase failure for '{purchaseId}': {ex}");
        return false;
    }
}
