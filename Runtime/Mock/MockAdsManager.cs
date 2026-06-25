using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Mock <see cref="IAdsManager"/> implementation.
/// Simulates ad SDK behavior: 1.5 s delay → onSuccess.
/// </summary>
public sealed class MockAdsManager : IAdsManager
{
    /// <inheritdoc/>
    public void Initialize()
    {
        Debug.LogWarning("[Mock Ads] Initialized. No real ad SDK.");
    }

    /// <inheritdoc/>
    public async void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null)
    {
        Debug.Log($"[Mock Ads] Rewarded: simulating view ({placementId})...");
        await Task.Delay(1500);
        Debug.Log("[Mock Ads] Rewarded: view complete → onSuccess.");
        onSuccess?.Invoke();
    }

    /// <inheritdoc/>
    public void ShowInterstitial(string placementId)
    {
        Debug.Log($"[Mock Ads] Interstitial: shown ({placementId}) (mock).");
    }
}
