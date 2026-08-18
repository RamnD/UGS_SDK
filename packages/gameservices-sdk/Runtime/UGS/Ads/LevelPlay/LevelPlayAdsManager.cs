using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.LevelPlay;
using UnityEngine;

/// <summary>
/// <see cref="IAdsManager"/> implementation via Unity LevelPlay SDK 8.x (formerly IronSource).
/// Recommended path for new projects — supports mediation (Unity Ads, Meta, AppLovin, Pangle, etc.).
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
    enum InitState
    {
        NotStarted,
        InProgress,
        Succeeded,
        Failed,
    }

    private readonly string _appKey;
    private InitState _initState = InitState.NotStarted;

    // Cached ad unit instances — created on first use
    private readonly Dictionary<string, LevelPlayRewardedAd>    _rewardedAds    = new();
    private readonly Dictionary<string, LevelPlayInterstitialAd> _interstitials  = new();

    // Current rewarded show state (only one at a time)
    private Action _pendingSuccess;
    private Action _pendingFailed;
    private string _activeRewardedUnitId;
    private bool _rewardEarned;
    private bool _rewardedClosed;
    private int _rewardedGeneration;
    private int _activeRewardedGeneration;
    private const int LateRewardGraceMs = 2000;

    // Current interstitial show state (only one at a time)
    private Action _pendingInterstitialClosed;
    private Action _pendingInterstitialFailed;
    private string _activeInterstitialUnitId;

    // Show requests that arrive before LevelPlay.Init finishes (async on device).
    private PendingRewardedShow _deferredRewardedShow;
    private PendingInterstitialShow _deferredInterstitialShow;

    struct PendingRewardedShow
    {
        public string PlacementId;
        public Action OnSuccess;
        public Action OnFailed;
    }

    struct PendingInterstitialShow
    {
        public string PlacementId;
        public Action OnClosed;
        public Action OnFailed;
    }

    /// <param name="appKey">App Key from LevelPlay Dashboard → Apps → your app.</param>
    public LevelPlayAdsManager(string appKey)
    {
        _appKey = appKey;
    }

    // ─── IAdsManager ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Initialize()
    {
        if (_initState != InitState.NotStarted)
        {
            AppLog.Info("LevelPlay", "Initialize skipped — already started.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_appKey))
        {
            _initState = InitState.Failed;
            AppLog.Error("LevelPlay", "Initialize failed: app key is empty.");
            return;
        }

        _initState = InitState.InProgress;
        LevelPlay.OnInitSuccess += OnInitSuccess;
        LevelPlay.OnInitFailed  += OnInitFailed;

#if UGS_ENV_STAGING || UGS_ENV_DEVELOPMENT
        // Integration Helper: logs network VERIFIED/MISSING and prints Advertising ID
        // (IDFA/GAID) for LevelPlay Dashboard → Setup → Testing → Add test device.
        AppLog.Info("LevelPlay", "ValidateIntegration (staging/development)...");
        LevelPlay.SetAdaptersDebug(true);
        LevelPlay.ValidateIntegration();
#endif

        AppLog.Info("LevelPlay", "Initializing SDK...");
        LevelPlay.Init(_appKey);
    }

    /// <inheritdoc/>
    public void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null)
    {
        if (!NetworkStatus.IsOnline)
        {
            AppLog.Warn("LevelPlay", "ShowRewardedAd skipped — offline / soft-offline.");
            onFailed?.Invoke();
            return;
        }

        if (_initState == InitState.NotStarted)
            Initialize();

        if (_initState == InitState.InProgress)
        {
            if (!string.IsNullOrWhiteSpace(_deferredRewardedShow.PlacementId))
            {
                AppLog.Warn("LevelPlay", "Rewarded already deferred — rejecting new show.");
                onFailed?.Invoke();
                return;
            }

            _deferredRewardedShow = new PendingRewardedShow
            {
                PlacementId = placementId,
                OnSuccess = onSuccess,
                OnFailed = onFailed,
            };
            return;
        }

        if (_initState == InitState.Failed)
        {
            AppLog.Warn("LevelPlay", "ShowRewardedAd: SDK init failed.");
            onFailed?.Invoke();
            return;
        }

        if (_activeRewardedUnitId != null)
        {
            AppLog.Warn("LevelPlay", $"Rewarded already in progress ({_activeRewardedUnitId}) — rejecting '{placementId}'.");
            onFailed?.Invoke();
            return;
        }

        var adUnitId = placementId;
        var ad       = GetOrCreateRewarded(adUnitId);

        _pendingSuccess       = onSuccess;
        _pendingFailed        = onFailed;
        _activeRewardedUnitId = adUnitId;
        _rewardEarned         = false;
        _rewardedClosed       = false;
        _rewardedGeneration++;
        _activeRewardedGeneration = _rewardedGeneration;

        if (ad.IsAdReady())
            ad.ShowAd();
        else
            ad.LoadAd(); // → OnAdLoaded → ShowAd
    }

    /// <inheritdoc/>
    public void ShowInterstitial(string placementId, Action onClosed = null, Action onFailed = null)
    {
        if (_initState == InitState.NotStarted)
            Initialize();

        if (_initState == InitState.InProgress)
        {
            if (!string.IsNullOrWhiteSpace(_deferredInterstitialShow.PlacementId))
            {
                AppLog.Warn("LevelPlay", "Interstitial already deferred — rejecting new show.");
                onFailed?.Invoke();
                return;
            }

            _deferredInterstitialShow = new PendingInterstitialShow
            {
                PlacementId = placementId,
                OnClosed = onClosed,
                OnFailed = onFailed,
            };
            return;
        }

        if (_initState == InitState.Failed)
        {
            AppLog.Warn("LevelPlay", "ShowInterstitial: SDK init failed.");
            onFailed?.Invoke();
            return;
        }

        if (_activeInterstitialUnitId != null)
        {
            AppLog.Warn("LevelPlay", $"Interstitial already in progress ({_activeInterstitialUnitId}) — rejecting '{placementId}'.");
            onFailed?.Invoke();
            return;
        }

        var adUnitId = placementId;
        var ad       = GetOrCreateInterstitial(adUnitId);

        _pendingInterstitialClosed = onClosed;
        _pendingInterstitialFailed = onFailed;
        _activeInterstitialUnitId  = adUnitId;

        if (ad.IsAdReady())
        {
            ad.ShowAd();
#if UNITY_EDITOR
            ScheduleEditorInterstitialFallbackClose(adUnitId);
#endif
        }
        else
            ad.LoadAd(); // → OnAdLoaded → ShowAd
    }

    // ─── Init callbacks ───────────────────────────────────────────────────────

    private void OnInitSuccess(LevelPlayConfiguration config)
    {
        _initState = InitState.Succeeded;
        AppLog.Info("LevelPlay", $"Initialized. {config}");
        FlushDeferredShows();
    }

    private void OnInitFailed(LevelPlayInitError error)
    {
        _initState = InitState.Failed;
        AppLog.Error("LevelPlay", $"Init failed: {error}");

        var deferredRewarded = _deferredRewardedShow;
        _deferredRewardedShow = default;
        deferredRewarded.OnFailed?.Invoke();

        var deferredInterstitial = _deferredInterstitialShow;
        _deferredInterstitialShow = default;
        deferredInterstitial.OnFailed?.Invoke();
    }

    private void FlushDeferredShows()
    {
        var deferredRewarded = _deferredRewardedShow;
        if (!string.IsNullOrWhiteSpace(deferredRewarded.PlacementId))
        {
            _deferredRewardedShow = default;
            ShowRewardedAd(deferredRewarded.PlacementId, deferredRewarded.OnSuccess, deferredRewarded.OnFailed);
        }

        var deferredInterstitial = _deferredInterstitialShow;
        if (!string.IsNullOrWhiteSpace(deferredInterstitial.PlacementId))
        {
            _deferredInterstitialShow = default;
            ShowInterstitial(deferredInterstitial.PlacementId, deferredInterstitial.OnClosed, deferredInterstitial.OnFailed);
        }
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
        AppLog.Info("LevelPlay", $"Rewarded loaded: {adUnitId}");
        // Show only if this unit was requested via ShowRewardedAd
        if (adUnitId == _activeRewardedUnitId)
            _rewardedAds[adUnitId].ShowAd();
    }

    private void OnRewardedLoadFailed(string adUnitId, LevelPlayAdError error)
    {
        AppLog.Warn("LevelPlay", $"Rewarded load failed ({adUnitId}): {error}");
        if (adUnitId == _activeRewardedUnitId)
            InvokeFailedAndReset();
    }

    private void OnRewardedDisplayFailed(string adUnitId, LevelPlayAdError error)
    {
        AppLog.Warn("LevelPlay", $"Rewarded display failed ({adUnitId}): {error}");
        if (adUnitId == _activeRewardedUnitId)
            InvokeFailedAndReset();
    }

    private void OnRewardEarned(string adUnitId)
    {
        if (adUnitId != _activeRewardedUnitId)
            return;

        // Reject stale adapter rewards from a previous show (generation bumped on reset / new show).
        if (_activeRewardedGeneration != _rewardedGeneration)
            return;

        // Session already completed/failed — ignore late mediation callbacks.
        if (_pendingSuccess == null && _pendingFailed == null)
            return;

        if (_rewardEarned)
            return;

        _rewardEarned = true;
        var callback = _pendingSuccess;
        _pendingSuccess = null;
        _pendingFailed = null;
        callback?.Invoke();

        if (_rewardedClosed)
            FinishRewardedSession(adUnitId);
    }

    private void OnRewardedClosed(string adUnitId)
    {
        if (adUnitId != _activeRewardedUnitId)
        {
            if (_rewardedAds.TryGetValue(adUnitId, out var preloadMiss))
                preloadMiss.LoadAd();
            return;
        }

        _rewardedClosed = true;

        if (_rewardEarned)
        {
            FinishRewardedSession(adUnitId);
            return;
        }

        // Adapter may deliver OnAdRewarded after OnAdClosed — short grace before fail.
        int generation = _rewardedGeneration;
        WaitForLateRewardThenFailAsync(adUnitId, generation);
    }

    async void WaitForLateRewardThenFailAsync(string adUnitId, int generation)
    {
        try
        {
            await Task.Delay(LateRewardGraceMs);
        }
        catch
        {
            return;
        }

        if (generation != _rewardedGeneration)
            return;

        if (_rewardEarned)
        {
            FinishRewardedSession(adUnitId);
            return;
        }

        if (adUnitId == _activeRewardedUnitId || _rewardedClosed)
            InvokeFailedAndReset();

        if (_rewardedAds.TryGetValue(adUnitId, out var ad))
            ad.LoadAd();
    }

    void FinishRewardedSession(string adUnitId)
    {
        ResetCallbacks();
        if (_rewardedAds.TryGetValue(adUnitId, out var ad))
            ad.LoadAd();
    }

    // ─── Interstitial ad ─────────────────────────────────────────────────────

    private LevelPlayInterstitialAd GetOrCreateInterstitial(string adUnitId)
    {
        if (_interstitials.TryGetValue(adUnitId, out var existing))
            return existing;

        var ad = new LevelPlayInterstitialAd(adUnitId);
        ad.OnAdLoaded        += _        => OnInterstitialLoaded(adUnitId);
        ad.OnAdLoadFailed    += err      => OnInterstitialLoadFailed(adUnitId, err);
        ad.OnAdDisplayFailed += (_, err) => OnInterstitialDisplayFailed(adUnitId, err);
        ad.OnAdClosed        += _        => OnInterstitialClosed(adUnitId);
        _interstitials[adUnitId] = ad;
        return ad;
    }

    private void OnInterstitialLoaded(string adUnitId)
    {
        AppLog.Info("LevelPlay", $"Interstitial loaded: {adUnitId}");
        if (adUnitId != _activeInterstitialUnitId)
            return;

        _interstitials[adUnitId].ShowAd();
#if UNITY_EDITOR
        ScheduleEditorInterstitialFallbackClose(adUnitId);
#endif
    }

    private void OnInterstitialLoadFailed(string adUnitId, LevelPlayAdError error)
    {
        AppLog.Warn("LevelPlay", $"Interstitial load failed ({adUnitId}): {error}");
        if (adUnitId == _activeInterstitialUnitId)
            InvokeInterstitialFailedAndReset();
    }

    private void OnInterstitialDisplayFailed(string adUnitId, LevelPlayAdError error)
    {
        AppLog.Warn("LevelPlay", $"Interstitial display failed ({adUnitId}): {error}");
        if (adUnitId == _activeInterstitialUnitId)
            InvokeInterstitialFailedAndReset();
    }

    private void OnInterstitialClosed(string adUnitId)
    {
        AppLog.Info("LevelPlay", $"Interstitial closed adUnitId={adUnitId}, active={_activeInterstitialUnitId}");

        if (adUnitId == _activeInterstitialUnitId)
        {
            var cb = _pendingInterstitialClosed;
            ResetInterstitialCallbacks();
            cb?.Invoke();
        }

        if (_interstitials.TryGetValue(adUnitId, out var ad))
            ad.LoadAd(); // preload next
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void InvokeFailedAndReset()
    {
        var callback = _pendingFailed;
        ResetCallbacks();
        callback?.Invoke();
    }

    private void InvokeInterstitialFailedAndReset()
    {
        var callback = _pendingInterstitialFailed;
        ResetInterstitialCallbacks();
        callback?.Invoke();
    }

    private void ResetCallbacks()
    {
        _pendingSuccess       = null;
        _pendingFailed        = null;
        _activeRewardedUnitId = null;
        _rewardEarned         = false;
        _rewardedClosed       = false;
        // Invalidate in-flight grace timers and late OnAdRewarded for this session.
        _rewardedGeneration++;
    }

    private void ResetInterstitialCallbacks()
    {
        _pendingInterstitialClosed = null;
        _pendingInterstitialFailed = null;
        _activeInterstitialUnitId = null;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor interstitial mock has no auto-close countdown (unlike rewarded mock).
    /// If the mock UI is invisible or the Close button is missed, unblock gameplay.
    /// </summary>
    async void ScheduleEditorInterstitialFallbackClose(string adUnitId)
    {
        const int fallbackMs = 5000;
        await Task.Delay(fallbackMs);
        if (adUnitId != _activeInterstitialUnitId)
            return;

        AppLog.Warn("LevelPlay", $"Editor interstitial auto-closed after {fallbackMs}ms " +
            $"(mock OnAdClosed fallback): {adUnitId}");

        // Fallback only invokes the C# callback — it does not call InterstitialPrefab.HideAd(),
        // so m_Preview stays true and the fullscreen OnGUI overlay keeps blocking input.
        DismissEditorInterstitialVisual(adUnitId);
        OnInterstitialClosed(adUnitId);
    }

    void DismissEditorInterstitialVisual(string adUnitId)
    {
        if (!_interstitials.TryGetValue(adUnitId, out var ad))
            return;

        ad.DestroyAd();
        _interstitials.Remove(adUnitId);
    }
#endif
}
