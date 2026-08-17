/// <summary>
/// Sticky vs one-shot presentation policy for <see cref="ServiceFaultPool"/>.
/// </summary>
public static class ServiceFaultPolicy
{
    public static bool IsSticky(ServiceFaultDomain domain, string faultKey)
    {
        if (string.IsNullOrWhiteSpace(faultKey))
            return false;

        if (domain == ServiceFaultDomain.Network
            && faultKey == ServiceFaultKeys.NetworkOffline)
            return true;

        if (domain == ServiceFaultDomain.Ads
            && faultKey == ServiceFaultKeys.AdsInitFailed)
            return true;

        return false;
    }
}
