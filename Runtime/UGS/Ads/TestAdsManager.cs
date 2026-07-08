using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Stub <see cref="IAdsManager"/> without a real SDK.
/// Prefer <see cref="MockAdsManager"/> for editor tests.
/// </summary>
public class TestAdsManager : IAdsManager
{
    /// <inheritdoc/>
    public void Initialize()
    {
        Debug.LogWarning("[Ads] TestAdsManager initialized. No real SDK.");
    }

    /// <inheritdoc/>
    public async void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null)
    {
        Debug.Log($"[Ads] Rewarded: simulating view ({placementId})...");
        await Task.Delay(1500);
        Debug.Log("[Ads] Rewarded: view complete, grant reward.");
        onSuccess?.Invoke();
    }

    /// <inheritdoc/>
    public void ShowInterstitial(string placementId, Action onClosed = null, Action onFailed = null)
    {
        Debug.Log($"[Ads] Interstitial: simulating show ({placementId}).");
        onClosed?.Invoke();
    }
}
