#if RAMND_LEGACY_UNITY_ADS
using System;
using UnityEngine;
using UnityEngine.Advertisements;

/// <summary>
/// <b>Legacy:</b> <see cref="IAdsManager"/> implementation via Unity Ads SDK 4.x (no mediation).
/// <para>
/// For new projects use <see cref="LevelPlayAdsManager"/> — supports mediation
/// (Unity Ads + Meta + AppLovin, etc.) and is actively maintained.
/// </para>
/// <para>
/// Requires: Package Manager → <c>com.unity.ads</c> version 4.x or newer.
/// Game ID is set in Project Settings → Monetization → Unity Ads.
/// </para>
/// Bootstrap usage:
/// <code>
/// new UGSServicesBuilder()
///     .WithAds(new UnityAdsManager("androidGameId", "iosGameId", testMode: false))
///     .BuildAsync();
/// </code>
/// </summary>
public sealed class UnityAdsManager : IAdsManager,
    IUnityAdsInitializationListener,
    IUnityAdsLoadListener,
    IUnityAdsShowListener
{
    private readonly string _androidGameId;
    private readonly string _iosGameId;
    private readonly bool   _testMode;

    // Callbacks for the current rewarded show — held for the duration of Show()
    private Action _pendingSuccess;
    private Action _pendingFailed;
    // Current show ID — resolves LoadListener/ShowListener collisions
    private string _activeShowPlacementId;

    /// <param name="androidGameId">Unity Ads Game ID for Android (Project Settings → Ads).</param>
    /// <param name="iosGameId">Unity Ads Game ID for iOS (Project Settings → Ads).</param>
    /// <param name="testMode">Enable test mode. Use false in production.</param>
    public UnityAdsManager(string androidGameId, string iosGameId, bool testMode = false)
    {
        _androidGameId = androidGameId;
        _iosGameId     = iosGameId;
        _testMode      = testMode;
    }

    // ─── IAdsManager ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Initialize()
    {
        if (Advertisement.isInitialized)
        {
            Debug.Log("[Ads] Unity Ads already initialized.");
            return;
        }

#if UNITY_ANDROID
        Advertisement.Initialize(_androidGameId, _testMode, this);
#elif UNITY_IOS
        Advertisement.Initialize(_iosGameId, _testMode, this);
#else
        Debug.LogWarning("[Ads] UnityAdsManager: unsupported platform, init skipped.");
#endif
    }

    /// <inheritdoc/>
    public void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null)
    {
        if (!Advertisement.isInitialized)
        {
            Debug.LogWarning("[Ads] Rewarded: SDK not initialized.");
            onFailed?.Invoke();
            return;
        }

        _pendingSuccess          = onSuccess;
        _pendingFailed           = onFailed;
        _activeShowPlacementId   = placementId;

        Advertisement.Load(_activeShowPlacementId, this);
    }

    /// <inheritdoc/>
    public void ShowInterstitial(string placementId)
    {
        if (!Advertisement.isInitialized)
        {
            Debug.LogWarning("[Ads] Interstitial: SDK not initialized.");
            return;
        }

        Advertisement.Show(placementId, this);
    }

    // ─── IUnityAdsInitializationListener ─────────────────────────────────────

    /// <summary>Unity Ads SDK initialized successfully.</summary>
    public void OnInitializationComplete()
    {
        Debug.Log("[Ads] Unity Ads initialized.");
    }

    /// <summary>SDK initialization error.</summary>
    public void OnInitializationFailed(UnityAdsInitializationError error, string message)
    {
        Debug.LogError($"[Ads] Init failed: {error} — {message}");
    }

    // ─── IUnityAdsLoadListener ────────────────────────────────────────────────

    /// <summary>Ad loaded — start show.</summary>
    public void OnUnityAdsAdLoaded(string placementId)
    {
        Debug.Log($"[Ads] Loaded: {placementId}");
        // Show only the placement we requested (collision guard)
        if (placementId == _activeShowPlacementId)
            Advertisement.Show(placementId, this);
    }

    /// <summary>Ad load error.</summary>
    public void OnUnityAdsFailedToLoad(string placementId, UnityAdsLoadError error, string message)
    {
        Debug.LogWarning($"[Ads] Failed to load {placementId}: {error} — {message}");
        if (placementId == _activeShowPlacementId)
            InvokeFailedAndReset();
    }

    // ─── IUnityAdsShowListener ────────────────────────────────────────────────

    /// <summary>Player started watching.</summary>
    public void OnUnityAdsShowStart(string placementId) { }

    /// <summary>Ad click.</summary>
    public void OnUnityAdsShowClick(string placementId) { }

    /// <summary>
    /// Show finished. <see cref="ShowResult.Finished"/> = player watched to the end → call onSuccess.
    /// Any other result (Skipped, Failed) → onFailed.
    /// </summary>
    public void OnUnityAdsShowComplete(string placementId, UnityAdsShowCompletionState showCompletionState)
    {
        Debug.Log($"[Ads] Show complete: {placementId}, result: {showCompletionState}");

        if (placementId != _activeShowPlacementId)
            return;

        if (showCompletionState == UnityAdsShowCompletionState.COMPLETED)
        {
            var callback = _pendingSuccess;
            ResetCallbacks();
            callback?.Invoke();
        }
        else
        {
            InvokeFailedAndReset();
        }
    }

    /// <summary>Error during ad show.</summary>
    public void OnUnityAdsShowFailure(string placementId, UnityAdsShowError error, string message)
    {
        Debug.LogError($"[Ads] Show failed {placementId}: {error} — {message}");
        if (placementId == _activeShowPlacementId)
            InvokeFailedAndReset();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void InvokeFailedAndReset()
    {
        var callback = _pendingFailed;
        ResetCallbacks();
        callback?.Invoke();
    }

    private void ResetCallbacks()
    {
        _pendingSuccess        = null;
        _pendingFailed         = null;
        _activeShowPlacementId = null;
    }
}
#endif // RAMND_LEGACY_UNITY_ADS
