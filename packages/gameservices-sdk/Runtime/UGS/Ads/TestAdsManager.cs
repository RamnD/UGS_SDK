using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Stub <see cref="IAdsManager"/> without a real SDK.
/// Prefer <see cref="MockAdsManager"/> for editor tests.
/// Only grants simulated rewards in Editor / Development builds.
/// </summary>
public class TestAdsManager : IAdsManager
{
    /// <inheritdoc/>
    public void Initialize()
    {
        AppLog.Warn("Ads", "TestAdsManager initialized. No real SDK.");
    }

    /// <inheritdoc/>
    public async void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        AppLog.Info("Ads", $"Rewarded: simulating view ({placementId})...");
        try
        {
            await Task.Delay(1500);
            AppLog.Info("Ads", "Rewarded: view complete, grant reward.");
            onSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Error("Ads", $"Test rewarded simulation failed: {ex.Message}");
            onFailed?.Invoke();
        }
#else
        AppLog.Error("Ads", $"TestAdsManager.ShowRewardedAd called in non-dev build ({placementId}) — failing.");
        onFailed?.Invoke();
#endif
    }

    /// <inheritdoc/>
    public void ShowInterstitial(string placementId, Action onClosed = null, Action onFailed = null)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        AppLog.Info("Ads", $"Interstitial: simulating show ({placementId}).");
        onClosed?.Invoke();
#else
        AppLog.Error("Ads", $"TestAdsManager.ShowInterstitial called in non-dev build ({placementId}) — failing.");
        onFailed?.Invoke();
#endif
    }
}
