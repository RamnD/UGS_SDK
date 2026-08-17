using System;
using UnityEngine;

/// <summary>
/// Texts + status sprites for service faults. Assign on <see cref="ServiceFaultHost"/>.
/// Missing keys fall back to English defaults in <see cref="ServiceFaultPool"/>.
/// </summary>
[CreateAssetMenu(fileName = "ServiceFaultCatalog", menuName = "RamnD/Service Fault Catalog")]
public sealed class ServiceFaultCatalog : ScriptableObject, IServiceFaultCatalog
{
    [Serializable]
    public struct Entry
    {
        public ServiceFaultDomain Domain;
        public string FaultKey;
        public ServiceFaultStatus Status;
        public string Title;
        public string Description;
        [Tooltip("Optional fixed code shown in UI (e.g. NET_OFFLINE). Raw reporter code overrides when non-empty.")]
        public string CodeHint;
        [Tooltip("Optional per-entry icon; otherwise status sprite from catalog is used.")]
        public Sprite IconOverride;
    }

    [SerializeField] Entry[] _entries = Array.Empty<Entry>();

    [Header("Status sprites (UI binds by ServiceFaultStatus)")]
    [SerializeField] Sprite _infoSprite;
    [SerializeField] Sprite _successSprite;
    [SerializeField] Sprite _warningSprite;
    [SerializeField] Sprite _errorSprite;

    public ReadOnlySpan<Entry> Entries => _entries;

    public bool TryResolve(
        ServiceFaultDomain domain,
        string faultKey,
        string rawCode,
        out ServiceFaultStatus status,
        out string title,
        out string description,
        out string code,
        out Sprite icon)
    {
        status = ServiceFaultStatus.Error;
        title = null;
        description = null;
        code = rawCode;
        icon = null;

        if (string.IsNullOrWhiteSpace(faultKey))
            return false;

        for (int i = 0; i < _entries.Length; i++)
        {
            Entry entry = _entries[i];
            if (entry.Domain != domain)
                continue;
            if (!string.Equals(entry.FaultKey, faultKey, StringComparison.Ordinal))
                continue;

            status = entry.Status;
            title = string.IsNullOrWhiteSpace(entry.Title)
                ? FallbackTitle(domain, faultKey)
                : entry.Title;
            description = string.IsNullOrWhiteSpace(entry.Description)
                ? FallbackDescription(domain, faultKey)
                : entry.Description;

            if (string.IsNullOrWhiteSpace(code))
                code = entry.CodeHint;

            icon = entry.IconOverride != null
                ? entry.IconOverride
                : GetStatusSprite(status);
            return true;
        }

        return false;
    }

    public Sprite GetStatusSprite(ServiceFaultStatus status) =>
        status switch
        {
            ServiceFaultStatus.Info => _infoSprite,
            ServiceFaultStatus.Success => _successSprite,
            ServiceFaultStatus.Warning => _warningSprite,
            _ => _errorSprite,
        };

    public static string FallbackTitle(ServiceFaultDomain domain, string faultKey) =>
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

    public static string FallbackDescription(ServiceFaultDomain domain, string faultKey) =>
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
                "Could not update your balance. Progress is safe — try again later.",
            ServiceFaultDomain.CloudSave =>
                "Could not sync cloud save. Your local progress is kept.",
            _ => "Something went wrong. Please try again.",
        };

    public static string FallbackCode(ServiceFaultDomain domain, string faultKey, string rawCode)
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
