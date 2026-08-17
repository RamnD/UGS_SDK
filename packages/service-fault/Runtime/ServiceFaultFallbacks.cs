/// <summary>
/// English safety-net copy when a catalog does not resolve a fault.
/// Games should override via <see cref="IServiceFaultCatalog"/> (localized strings, custom keys).
/// </summary>
public static class ServiceFaultFallbacks
{
    public static string Title(ServiceFaultDomain domain, string faultKey) =>
        domain switch
        {
            ServiceFaultDomain.Ads when faultKey == ServiceFaultKeys.AdsShowTimeout =>
                "Ad timed out",
            ServiceFaultDomain.Ads when faultKey == ServiceFaultKeys.AdsUnavailable =>
                "Ad unavailable",
            ServiceFaultDomain.Ads when faultKey == ServiceFaultKeys.AdsInitFailed =>
                "Ads unavailable",
            ServiceFaultDomain.Purchases when faultKey == ServiceFaultKeys.PurchasesNotReady =>
                "Store not ready",
            ServiceFaultDomain.Purchases when faultKey == ServiceFaultKeys.PurchasesIndeterminate =>
                "Purchase pending",
            ServiceFaultDomain.Purchases =>
                "Purchase failed",
            ServiceFaultDomain.Auth when faultKey == ServiceFaultKeys.AuthNotReady =>
                "Account not ready",
            ServiceFaultDomain.Auth =>
                "Account error",
            ServiceFaultDomain.Network =>
                "No connection",
            ServiceFaultDomain.Economy =>
                "Economy error",
            ServiceFaultDomain.CloudSave =>
                "Cloud save error",
            _ => "Something went wrong",
        };

    public static string Description(ServiceFaultDomain domain, string faultKey) =>
        domain switch
        {
            ServiceFaultDomain.Ads when faultKey == ServiceFaultKeys.AdsShowTimeout =>
                "The ad took too long to load. Please try again later.",
            ServiceFaultDomain.Ads when faultKey == ServiceFaultKeys.AdsUnavailable =>
                "Rewarded ad is not available right now. Check your connection and try again.",
            ServiceFaultDomain.Ads when faultKey == ServiceFaultKeys.AdsInitFailed =>
                "Ads service failed to start. Some rewards may be unavailable.",
            ServiceFaultDomain.Purchases when faultKey == ServiceFaultKeys.PurchasesNotReady =>
                "The store is still starting. Please wait a moment and try again.",
            ServiceFaultDomain.Purchases when faultKey == ServiceFaultKeys.PurchasesIndeterminate =>
                "Payment may have gone through. Check your balance — items usually appear within a minute. If not, try again later.",
            ServiceFaultDomain.Purchases =>
                "Purchase could not be completed. No charges were made — try again later.",
            ServiceFaultDomain.Auth when faultKey == ServiceFaultKeys.AuthNotReady =>
                "Account service is not ready yet. Try again in a moment.",
            ServiceFaultDomain.Auth =>
                "Could not link your account. Please try again.",
            ServiceFaultDomain.Network =>
                "No internet connection. Some features will work offline until you reconnect.",
            ServiceFaultDomain.Economy =>
                "Could not update your balance. Progress is safe. Gold and items may show as 0 until this recovers — reopen the game or wait a moment.",
            ServiceFaultDomain.CloudSave =>
                "Could not sync cloud save. Your local progress is kept.",
            _ => "Something went wrong. Please try again.",
        };

    public static string Code(ServiceFaultDomain domain, string faultKey, string rawCode)
    {
        if (!string.IsNullOrWhiteSpace(rawCode))
            return rawCode;

        return domain switch
        {
            ServiceFaultDomain.Ads => $"ADS_{faultKey}".ToUpperInvariant(),
            ServiceFaultDomain.Purchases => $"IAP_{faultKey}".ToUpperInvariant(),
            ServiceFaultDomain.Auth => $"AUTH_{faultKey}".ToUpperInvariant(),
            ServiceFaultDomain.Network => "NET_OFFLINE",
            ServiceFaultDomain.Economy => $"ECO_{faultKey}".ToUpperInvariant(),
            ServiceFaultDomain.CloudSave => $"SAVE_{faultKey}".ToUpperInvariant(),
            _ => $"{domain}_{faultKey}".ToUpperInvariant(),
        };
    }
}
