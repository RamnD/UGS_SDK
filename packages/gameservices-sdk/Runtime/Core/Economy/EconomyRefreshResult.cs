/// <summary>
/// Outcome of the last <see cref="IInventoryService{TCurrency}.RefreshBalancesAsync"/>.
/// Distinguishes a live server snapshot from cache fallbacks so the game can warn
/// instead of presenting empty balances as truth.
/// </summary>
public enum EconomyRefreshResult
{
    None = 0,
    /// <summary>GetBalances applied from the server.</summary>
    ReachedServer,
    /// <summary>Pending deltas still blocked overwrite — local cache kept.</summary>
    KeptLocalPending,
    /// <summary>Device offline — PlayerPrefs cache loaded.</summary>
    OfflineCache,
    /// <summary>Online GetBalances failed recoverably — last known cache used.</summary>
    TransportFallback,
}
