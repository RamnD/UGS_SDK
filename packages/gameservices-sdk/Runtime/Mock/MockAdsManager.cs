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
        AppLog.Warn("MockAds", "Initialized. No real ad SDK.");
    }

    /// <inheritdoc/>
    public async void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null)
    {
        AppLog.DebugLog("MockAds", $"Rewarded: simulating view ({placementId})...");
        await Task.Delay(1500);
        AppLog.DebugLog("MockAds", "Rewarded: view complete → onSuccess.");
        onSuccess?.Invoke();
    }

    /// <inheritdoc/>
    public void ShowInterstitial(string placementId, Action onClosed = null, Action onFailed = null)
    {
        AppLog.DebugLog("MockAds", $"Interstitial: shown ({placementId}) (mock).");
        onClosed?.Invoke();
    }
}
