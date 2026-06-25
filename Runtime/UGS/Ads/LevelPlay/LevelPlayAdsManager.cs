using System;
using System.Collections.Generic;
using Unity.Services.LevelPlay;
using UnityEngine;

/// <summary>
/// <see cref="IAdsManager"/> implementation via Unity LevelPlay SDK 8.x (formerly IronSource).
/// Recommended path for new projects — supports mediation (Unity Ads, Meta, AppLovin, etc.).
/// <para>
/// Requires: Package Manager → <c>com.unity.services.levelplay</c> version 8.x or newer.
/// App Key comes from the LevelPlay Dashboard (not Project Settings).
/// </para>
/// <para>
/// Ad Unit IDs are strings (as in the LevelPlay Dashboard). On the game side, an enum plus string mapping is convenient.
/// </para>
/// Bootstrap usage:
/// <code>
/// new UGSServicesBuilder()
///     .WithAds(new LevelPlayAdsManager("your-app-key"))
///     .BuildAsync();
/// </code>
/// </summary>
public sealed class LevelPlayAdsManager : IAdsManager
{
    private readonly string _appKey;
    private bool _initialized;

    // Cached ad unit instances — created on first use
    private readonly Dictionary<string, LevelPlayRewardedAd>    _rewardedAds    = new();
    private readonly Dictionary<string, LevelPlayInterstitialAd> _interstitials  = new();

    // Current rewarded show state (only one at a time)
    private Action _pendingSuccess;
    private Action _pendingFailed;
    private string _activeRewardedUnitId;

    /// <param name="appKey">App Key from LevelPlay Dashboard → Apps → your app.</param>
    public LevelPlayAdsManager(string appKey)
    {
        _appKey = appKey;
    }

    // ─── IAdsManager ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Initialize()
    {
        if (_initialized)
        {
            Debug.Log("[LevelPlay] Already initialized.");
            return;
        }

        LevelPlay.OnInitSuccess += OnInitSuccess;
        LevelPlay.OnInitFailed  += OnInitFailed;

        // isAdManagerEnabled=false → Ad Units mode (recommended in SDK 8.x)
        LevelPlay.Init(_appKey);
    }

    /// <inheritdoc/>
    public void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null)
    {
        if (!_initialized)
        {
            Debug.LogWarning("[LevelPlay] ShowRewardedAd: SDK not initialized.");
            onFailed?.Invoke();
            return;
        }

        var adUnitId = placementId;
        var ad       = GetOrCreateRewarded(adUnitId);

        _pendingSuccess       = onSuccess;
        _pendingFailed        = onFailed;
        _activeRewardedUnitId = adUnitId;

        if (ad.IsAdReady())
            ad.ShowAd();
        else
            ad.LoadAd(); // → OnAdLoaded → ShowAd
    }

    /// <inheritdoc/>
    public void ShowInterstitial(string placementId)
    {
        if (!_initialized)
        {
            Debug.LogWarning("[LevelPlay] ShowInterstitial: SDK not initialized.");
            return;
        }

        var adUnitId = placementId;
        var ad       = GetOrCreateInterstitial(adUnitId);

        if (ad.IsAdReady())
            ad.ShowAd();
        else
            ad.LoadAd(); // → OnAdLoaded → ShowAd
    }

    // ─── Init callbacks ───────────────────────────────────────────────────────

    private void OnInitSuccess(LevelPlayConfiguration config)
    {
        _initialized = true;
        Debug.Log($"[LevelPlay] Initialized. {config}");
    }

    private void OnInitFailed(LevelPlayInitError error)
    {
        Debug.LogError($"[LevelPlay] Init failed: {error}");
    }

    // ─── Rewarded ad ─────────────────────────────────────────────────────────

    private LevelPlayRewardedAd GetOrCreateRewarded(string adUnitId)
    {
        if (_rewardedAds.TryGetValue(adUnitId, out var existing))
            return existing;

        var ad = new LevelPlayRewardedAd(adUnitId);
        ad.OnAdLoaded        += _    => OnRewardedLoaded(adUnitId);
        ad.OnAdLoadFailed    += err  => OnRewardedLoadFailed(adUnitId, err);
        ad.OnAdDisplayFailed += (_, err) => OnRewardedDisplayFailed(adUnitId, err);
        ad.OnAdRewarded      += (_, _) => OnRewardEarned(adUnitId);
        ad.OnAdClosed        += _    => OnRewardedClosed(adUnitId);
        _rewardedAds[adUnitId] = ad;
        return ad;
    }

    private void OnRewardedLoaded(string adUnitId)
    {
        Debug.Log($"[LevelPlay] Rewarded loaded: {adUnitId}");
        // Show only if this unit was requested via ShowRewardedAd
        if (adUnitId == _activeRewardedUnitId)
            _rewardedAds[adUnitId].ShowAd();
    }

    private void OnRewardedLoadFailed(string adUnitId, LevelPlayAdError error)
    {
        Debug.LogWarning($"[LevelPlay] Rewarded load failed ({adUnitId}): {error}");
        if (adUnitId == _activeRewardedUnitId)
            InvokeFailedAndReset();
    }

    private void OnRewardedDisplayFailed(string adUnitId, LevelPlayAdError error)
    {
        Debug.LogWarning($"[LevelPlay] Rewarded display failed ({adUnitId}): {error}");
        if (adUnitId == _activeRewardedUnitId)
            InvokeFailedAndReset();
    }

    private void OnRewardEarned(string adUnitId)
    {
        if (adUnitId != _activeRewardedUnitId) return;
        // Keep callback before reset — OnAdClosed may arrive after
        var callback = _pendingSuccess;
        ResetCallbacks(); // _activeRewardedUnitId → null before OnAdClosed
        callback?.Invoke();
    }

    private void OnRewardedClosed(string adUnitId)
    {
        // If OnAdRewarded has not fired yet (user skipped) → call onFailed
        if (adUnitId == _activeRewardedUnitId)
            InvokeFailedAndReset();

        // Preload the next video right after close
        if (_rewardedAds.TryGetValue(adUnitId, out var ad))
            ad.LoadAd();
    }

    // ─── Interstitial ad ─────────────────────────────────────────────────────

    private LevelPlayInterstitialAd GetOrCreateInterstitial(string adUnitId)
    {
        if (_interstitials.TryGetValue(adUnitId, out var existing))
            return existing;

        var ad = new LevelPlayInterstitialAd(adUnitId);
        ad.OnAdLoaded        += _        => { Debug.Log($"[LevelPlay] Interstitial loaded: {adUnitId}"); _interstitials[adUnitId].ShowAd(); };
        ad.OnAdLoadFailed    += err      => Debug.LogWarning($"[LevelPlay] Interstitial load failed ({adUnitId}): {err}");
        ad.OnAdDisplayFailed += (_, err) => Debug.LogWarning($"[LevelPlay] Interstitial display failed ({adUnitId}): {err}");
        ad.OnAdClosed        += _        => _interstitials[adUnitId].LoadAd(); // preload
        _interstitials[adUnitId] = ad;
        return ad;
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
        _pendingSuccess       = null;
        _pendingFailed        = null;
        _activeRewardedUnitId = null;
    }
}
