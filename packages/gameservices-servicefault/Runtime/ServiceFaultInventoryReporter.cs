/// <summary>
/// Maps <see cref="InventoryOperationException"/> and economy refresh outcomes
/// into <see cref="ServiceFaultPool"/> entries.
/// Soft spend failures and recoverable offline queues never throw, so only hard
/// service/runtime reasons should land here.
/// </summary>
public static class ServiceFaultInventoryReporter
{
    public static void Report(InventoryOperationException ex)
    {
        if (ex == null)
            return;

        switch (ex.Reason)
        {
            case InventoryFailureReason.NetworkUnavailable:
            case InventoryFailureReason.OperationNotAllowedOffline:
                ServiceFaultPool.Report(
                    ServiceFaultDomain.Network,
                    ServiceFaultKeys.NetworkOffline,
                    ex.Reason.ToString());
                break;

            case InventoryFailureReason.ProviderRejected:
            case InventoryFailureReason.PendingTransactionsFlushFailed:
            default:
                ServiceFaultPool.Report(
                    ServiceFaultDomain.Economy,
                    ServiceFaultKeys.EconomyFailed,
                    ex.Reason.ToString());
                break;
        }
    }

    /// <summary>
    /// Session or reconnect refresh did not reach a live server snapshot.
    /// Offline is reported under Network so transport fallback does not look like
    /// a true zero-balance economy state.
    /// </summary>
    public static void ReportRefreshOutcome(EconomyRefreshResult result)
    {
        switch (result)
        {
            case EconomyRefreshResult.TransportFallback:
                ServiceFaultPool.Report(
                    ServiceFaultDomain.Economy,
                    ServiceFaultKeys.EconomyFailed,
                    "ECO_REFRESH_FALLBACK");
                break;
            case EconomyRefreshResult.OfflineCache:
                ServiceFaultPool.Report(
                    ServiceFaultDomain.Network,
                    ServiceFaultKeys.NetworkOffline,
                    "ECO_OFFLINE_CACHE");
                break;
        }
    }

    public static bool DidRefreshReachServer(EconomyRefreshResult result) =>
        result == EconomyRefreshResult.ReachedServer;

    public static void ClearEconomyFaults() =>
        ServiceFaultPool.ClearDomain(ServiceFaultDomain.Economy);

    /// <summary>
    /// Call after a successful online-capable economy/cloud action so sticky offline
    /// state clears only when the SDK actually considers the link healthy.
    /// </summary>
    public static void ClearNetworkOfflineIfOnline()
    {
        if (NetworkStatus.IsOnline)
            ServiceFaultPool.Clear(ServiceFaultDomain.Network, ServiceFaultKeys.NetworkOffline);
    }
}
