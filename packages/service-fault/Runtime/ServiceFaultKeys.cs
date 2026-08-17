/// <summary>Stable catalog keys used by reporters. Keep in sync with catalog entries.</summary>
public static class ServiceFaultKeys
{
    public const string AdsUnavailable = "unavailable";
    public const string AdsShowTimeout = "show_timeout";
    public const string AdsInitFailed = "init_failed";

    public const string PurchasesFailed = "failed";
    public const string PurchasesNotReady = "not_ready";
    public const string PurchasesIndeterminate = "pending_verify";

    public const string AuthFailed = "failed";
    public const string AuthNotReady = "not_ready";

    public const string NetworkOffline = "offline";

    public const string EconomyFailed = "failed";
    public const string CloudSaveFailed = "failed";
}
