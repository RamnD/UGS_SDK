using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Stub-реализация <see cref="IAdsManager"/> без реального SDK.
/// Для редакторских тестов предпочтительнее <see cref="MockAdsManager"/>.
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
    public void ShowInterstitial(string placementId)
    {
        Debug.Log($"[Ads] Interstitial: simulating show ({placementId}).");
    }
}
