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
/// Ad Unit IDs are strings (as in the LevelPlay Dashboard). Placement names stay in the game.
/// Warm preload, skip-load-while-shown (error 629), and load-then-show live here.
/// No-ads / qualifying-watch policy is injected via <see cref="LevelPlayAdsOptions"/>.
/// </para>
/// Bootstrap usage:
/// <code>
/// new UGSServicesBuilder()
///     .WithAds(new LevelPlayAdsManager("your-app-key", options))
///     .BuildAsync();
/// // After ATT/CMP/COPPA when DeferInitUntilPrivacy:
/// ((ILevelPlayAdsController)ads).BeginSdkInitialization();
/// </code>
/// </summary>
public sealed class LevelPlayAdsManager : ILevelPlayAdsController
{
    private const int PreloadRetryDelayMs = 4000;
    /// <summary>LevelPlay: LoadAd while this ad instance is already on screen.</summary>
    private const int LevelPlayLoadWhileShownErrorCode = 629;

    private enum InitState
    {
        NotStarted,
        /// <summary>Registered; waiting for <see cref="BeginSdkInitialization"/> after privacy.</summary>
        AwaitingPrivacy,
        InProgress,
        Succeeded,
        Failed,
    }

    private readonly string _appKey;
    private readonly LevelPlayAdsOptions _options;
    private readonly int _qualifyingWatchMs;
    private readonly int _lateRewardGraceMs;
    private readonly int _loadThenShowTimeoutMs;
    private readonly int _deferredShowTimeoutMs;
    private InitState _initState = InitState.NotStarted;

    private readonly Dictionary<string, LevelPlayRewardedAd> _rewardedAds = new();
    private readonly Dictionary<string, LevelPlayInterstitialAd> _interstitials = new();
    private readonly HashSet<string> _preloadUnitIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingPreloadUnitIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preloadInterstitialUnitIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingPreloadInterstitialUnitIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preloadRetryInFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rewardedLoadInFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> _interstitialLoadInFlight = new(StringComparer.Ordinal);

    private Action _pendingSuccess;
    private Action _pendingFailed;
    private string _activeRewardedUnitId;
    private bool _rewardEarned;
    private bool _rewardedClosed;
    private bool _rewardedShowIssued;
    private bool _rewardedDisplayed;
    private float _rewardedDisplayRealtimeStart;
    private uint _rewardedSessionActivity;
    private bool _closeDeferredForStoreLeave;
    private bool _rewardedHadClick;
    private int _rewardedGeneration;
    private int _activeRewardedGeneration;

    private string _loadThenShowUnitId;
    private int _loadThenShowGeneration;

    private Action _pendingInterstitialClosed;
    private Action _pendingInterstitialFailed;
    private string _activeInterstitialUnitId;
    private bool _interstitialShowIssued;
    private bool _interstitialDisplayed;
    private string _interstitialLoadThenShowUnitId;
    private int _interstitialLoadThenShowGeneration;

    private PendingRewardedShow _deferredRewardedShow;
    private PendingInterstitialShow _deferredInterstitialShow;
    private int _deferredRewardedGeneration;
    private int _deferredInterstitialGeneration;

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
    /// <param name="options">Optional game hooks (no-ads bypass, privacy-deferred init, timings).</param>
    public LevelPlayAdsManager(string appKey, LevelPlayAdsOptions options = null)
    {
        _appKey = appKey;
        _options = options ?? new LevelPlayAdsOptions();
        _qualifyingWatchMs = Math.Max(0, _options.QualifyingWatchMs);
        _lateRewardGraceMs = Math.Max(0, _options.LateRewardGraceMs);
        _loadThenShowTimeoutMs = Math.Max(1, _options.LoadThenShowTimeoutMs);
        _deferredShowTimeoutMs = Math.Max(1, _options.DeferredShowTimeoutMs);
    }

    bool ShouldBypassAsSuccess()
    {
        try
        {
            return _options.ShouldBypassAsSuccess != null && _options.ShouldBypassAsSuccess();
        }
        catch (Exception ex)
        {
            AppLog.Warn("LevelPlay", $"ShouldBypassAsSuccess threw: {ex.Message}");
            return false;
        }
    }

    bool IsUnityBackgrounded()
    {
        if (_options.IsUnityBackgrounded != null)
        {
            try
            {
                return _options.IsUnityBackgrounded();
            }
            catch (Exception ex)
            {
                AppLog.Warn("LevelPlay", $"IsUnityBackgrounded threw: {ex.Message}");
            }
        }

        return !Application.isFocused;
    }

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

        if (_options.DeferInitUntilPrivacy)
        {
            _initState = InitState.AwaitingPrivacy;
            AppLog.Info("LevelPlay", "Registered — awaiting privacy bootstrap before Init.");
            return;
        }

        StartSdkInitialization();
    }

    /// <inheritdoc/>
    public void BeginSdkInitialization()
    {
        if (_initState == InitState.InProgress ||
            _initState == InitState.Succeeded ||
            _initState == InitState.Failed)
        {
            AppLog.Info("LevelPlay", "BeginSdkInitialization skipped — already started.");
            return;
        }

        if (_initState == InitState.NotStarted)
            Initialize();

        if (_initState == InitState.Failed)
            return;

        if (_initState == InitState.InProgress || _initState == InitState.Succeeded)
            return;

        StartSdkInitialization();
    }

    void StartSdkInitialization()
    {
        if (string.IsNullOrWhiteSpace(_appKey))
        {
            _initState = InitState.Failed;
            AppLog.Error("LevelPlay", "BeginSdkInitialization failed: app key is empty.");
            return;
        }

        _initState = InitState.InProgress;
        LevelPlay.OnInitSuccess += OnInitSuccess;
        LevelPlay.OnInitFailed += OnInitFailed;

#if UGS_ENV_STAGING || UGS_ENV_DEVELOPMENT
        AppLog.Info("LevelPlay", "ValidateIntegration (staging/development)...");
        LevelPlay.SetAdaptersDebug(true);
        LevelPlay.ValidateIntegration();
#endif

        AppLog.Info("LevelPlay", "Initializing SDK...");
        LevelPlay.Init(_appKey);
    }

    /// <inheritdoc/>
    public void PreloadRewardedUnits(params string[] adUnitIds)
    {
        if (adUnitIds == null)
            return;

        for (int i = 0; i < adUnitIds.Length; i++)
            PreloadRewarded(adUnitIds[i]);
    }

    public void PreloadRewarded(string adUnitId)
    {
        if (string.IsNullOrWhiteSpace(adUnitId))
            return;

        _preloadUnitIds.Add(adUnitId);

        if (_initState == InitState.NotStarted ||
            _initState == InitState.AwaitingPrivacy ||
            _initState == InitState.InProgress)
        {
            if (_initState == InitState.NotStarted)
                Initialize();

            _pendingPreloadUnitIds.Add(adUnitId);
            return;
        }

        if (_initState != InitState.Succeeded)
            return;

        LevelPlayRewardedAd ad = GetOrCreateRewarded(adUnitId);
        if (ad.IsAdReady())
            return;

        if (IsRewardedNativeUp(adUnitId))
        {
            AppLog.Info("LevelPlay", $"Preload rewarded skipped — show in progress: {adUnitId}");
            return;
        }

        if (!_rewardedLoadInFlight.Add(adUnitId))
            return;

        AppLog.Info("LevelPlay", $"Preload rewarded: {adUnitId}");
        ad.LoadAd();
    }

    /// <inheritdoc/>
    public void EnsurePreloadedUnitsReady()
    {
        if (_initState != InitState.Succeeded)
            return;

        foreach (string adUnitId in _preloadUnitIds)
            PreloadRewarded(adUnitId);

        foreach (string adUnitId in _preloadInterstitialUnitIds)
            PreloadInterstitial(adUnitId);
    }

    /// <inheritdoc/>
    public void PreloadInterstitialUnits(params string[] adUnitIds)
    {
        if (adUnitIds == null)
            return;

        for (int i = 0; i < adUnitIds.Length; i++)
            PreloadInterstitial(adUnitIds[i]);
    }

    public void PreloadInterstitial(string adUnitId)
    {
        if (string.IsNullOrWhiteSpace(adUnitId))
            return;

        _preloadInterstitialUnitIds.Add(adUnitId);

        if (_initState == InitState.NotStarted ||
            _initState == InitState.AwaitingPrivacy ||
            _initState == InitState.InProgress)
        {
            if (_initState == InitState.NotStarted)
                Initialize();

            _pendingPreloadInterstitialUnitIds.Add(adUnitId);
            return;
        }

        if (_initState != InitState.Succeeded)
            return;

        LevelPlayInterstitialAd ad = GetOrCreateInterstitial(adUnitId);
        if (ad.IsAdReady())
            return;

        if (IsInterstitialNativeUp(adUnitId))
        {
            AppLog.Info("LevelPlay", $"Preload interstitial skipped — show in progress: {adUnitId}");
            return;
        }

        if (!_interstitialLoadInFlight.Add(adUnitId))
            return;

        AppLog.Info("LevelPlay", $"Preload interstitial: {adUnitId}");
        ad.LoadAd();
    }

    /// <inheritdoc/>
    public bool IsRewardedReady(string adUnitId)
    {
        if (string.IsNullOrWhiteSpace(adUnitId))
            return false;

        return _rewardedAds.TryGetValue(adUnitId, out LevelPlayRewardedAd ad) && ad.IsAdReady();
    }

    /// <inheritdoc/>
    public bool IsSdkInitializationFailed => _initState == InitState.Failed;

    /// <inheritdoc/>
    public bool AreRewardedUnitsReady(params string[] adUnitIds)
    {
        if (adUnitIds == null || adUnitIds.Length == 0)
            return true;

        bool any = false;
        for (int i = 0; i < adUnitIds.Length; i++)
        {
            string id = adUnitIds[i];
            if (string.IsNullOrWhiteSpace(id))
                continue;

            any = true;
            if (!IsRewardedReady(id))
                return false;
        }

        return any;
    }

    /// <inheritdoc/>
    public bool IsInterstitialReady(string adUnitId)
    {
        if (string.IsNullOrWhiteSpace(adUnitId))
            return false;

        return _interstitials.TryGetValue(adUnitId, out LevelPlayInterstitialAd ad) && ad.IsAdReady();
    }

    /// <inheritdoc/>
    public bool AreInterstitialUnitsReady(params string[] adUnitIds)
    {
        if (adUnitIds == null || adUnitIds.Length == 0)
            return true;

        bool any = false;
        for (int i = 0; i < adUnitIds.Length; i++)
        {
            string id = adUnitIds[i];
            if (string.IsNullOrWhiteSpace(id))
                continue;

            any = true;
            if (!IsInterstitialReady(id))
                return false;
        }

        return any;
    }

    /// <inheritdoc/>
    public bool IsRewardedShowInProgress(string adUnitId)
    {
        if (string.IsNullOrWhiteSpace(adUnitId) || adUnitId != _activeRewardedUnitId)
            return false;

        return _rewardedShowIssued || _rewardedDisplayed;
    }

    /// <inheritdoc/>
    public bool IsInterstitialShowInProgress(string adUnitId)
    {
        if (string.IsNullOrWhiteSpace(adUnitId) || adUnitId != _activeInterstitialUnitId)
            return false;

        return _interstitialShowIssued || _interstitialDisplayed;
    }

    bool IsRewardedNativeUp(string adUnitId) =>
        adUnitId == _activeRewardedUnitId && (_rewardedShowIssued || _rewardedDisplayed);

    bool IsInterstitialNativeUp(string adUnitId) =>
        adUnitId == _activeInterstitialUnitId && (_interstitialShowIssued || _interstitialDisplayed);

    static bool IsLoadWhileShownError(LevelPlayAdError error)
    {
        if (error == null)
            return false;

        if (error.ErrorCode == LevelPlayLoadWhileShownErrorCode)
            return true;

        string message = error.ErrorMessage;
        return !string.IsNullOrEmpty(message) &&
            message.IndexOf("while ad is shown", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    bool HasQualifyingWatch()
    {
        if (_qualifyingWatchMs <= 0)
            return false;

        if (!_rewardedDisplayed || _rewardedDisplayRealtimeStart <= 0f)
            return false;

        float watchedMs = (Time.realtimeSinceStartup - _rewardedDisplayRealtimeStart) * 1000f;
        return watchedMs >= _qualifyingWatchMs;
    }

    /// <inheritdoc/>
    public void NotifyRewardedForegroundResume()
    {
        if (_activeRewardedUnitId == null)
            return;

        if (!_closeDeferredForStoreLeave)
            return;

        string unitId = _activeRewardedUnitId;
        int generation = _rewardedGeneration;
        FinalizeDeferredStoreCloseAsync(unitId, generation);
    }

    async void FinalizeDeferredStoreCloseAsync(string adUnitId, int generation)
    {
        try
        {
            await Task.Delay(1500);
        }
        catch
        {
            return;
        }

        if (generation != _rewardedGeneration)
            return;

        if (!_closeDeferredForStoreLeave || adUnitId != _activeRewardedUnitId)
            return;

        if (!_rewardEarned && !HasQualifyingWatch())
        {
            AppLog.Info(
                "LevelPlay",
                $"Deferred store-close on resume — still waiting for dismiss: {adUnitId}");
            return;
        }

        AppLog.Info(
            "LevelPlay",
            $"Rewarded deferred store-close finalized on resume: {adUnitId} earned={_rewardEarned}");
        _closeDeferredForStoreLeave = false;
        ResolveRewardedClosed(adUnitId);
    }

    /// <inheritdoc/>
    public string LastRewardedNetwork { get; private set; }

    /// <inheritdoc/>
    public string LastInterstitialNetwork { get; private set; }

    /// <inheritdoc/>
    public void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null)
    {
        if (ShouldBypassAsSuccess())
        {
            AppLog.Info("LevelPlay", "Rewarded bypassed (ShouldBypassAsSuccess).");
            onSuccess?.Invoke();
            return;
        }

        if (!NetworkStatus.IsOnline)
        {
            AppLog.Warn("LevelPlay", "ShowRewardedAd skipped — offline / soft-offline.");
            onFailed?.Invoke();
            return;
        }

        if (_initState == InitState.NotStarted)
            Initialize();

        if (_initState == InitState.AwaitingPrivacy || _initState == InitState.InProgress)
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
            DeferredRewardedShowTimeoutAsync(placementId, ++_deferredRewardedGeneration);
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
            AppLog.Warn(
                "LevelPlay",
                $"Rewarded already in progress ({_activeRewardedUnitId}) — rejecting '{placementId}'.");
            onFailed?.Invoke();
            return;
        }

        string adUnitId = placementId;
        LevelPlayRewardedAd ad = GetOrCreateRewarded(adUnitId);

        _pendingSuccess = onSuccess;
        _pendingFailed = onFailed;
        _activeRewardedUnitId = adUnitId;
        _rewardEarned = false;
        _rewardedClosed = false;
        _rewardedShowIssued = false;
        _rewardedDisplayed = false;
        _rewardedDisplayRealtimeStart = 0f;
        _rewardedSessionActivity = 0;
        _closeDeferredForStoreLeave = false;
        _rewardedHadClick = false;
        _rewardedGeneration++;
        _activeRewardedGeneration = _rewardedGeneration;
        LastRewardedNetwork = null;

        if (ad.IsAdReady())
        {
            AppLog.Info("LevelPlay", $"ShowRewardedAd ready: {adUnitId}");
            ClearLoadThenShowWait();
            IssueRewardedShow(ad, adUnitId);
        }
        else
        {
            AppLog.Info("LevelPlay", $"ShowRewardedAd not ready — load then show: {adUnitId}");
            BeginLoadThenShow(adUnitId);
            if (_rewardedLoadInFlight.Add(adUnitId))
                ad.LoadAd();
            else
                AppLog.Info("LevelPlay", $"ShowRewardedAd awaiting in-flight preload: {adUnitId}");
        }
    }

    /// <inheritdoc/>
    public void CancelPendingRewardedShow(string adUnitId)
    {
        if (string.IsNullOrWhiteSpace(adUnitId))
            return;

        if (TryDropDeferredRewardedShow(adUnitId, "cancel", out Action deferredFailed))
        {
            deferredFailed?.Invoke();
            return;
        }

        if (_activeRewardedUnitId != adUnitId && _loadThenShowUnitId != adUnitId)
            return;

        AppLog.Info("LevelPlay", $"CancelPendingRewardedShow: {adUnitId}");
        bool destroyNative = _rewardedShowIssued || _rewardedDisplayed;
        // ResetCallbacks also frees the in-flight load slot, so the next
        // PreloadRewarded on a fresh instance is not skipped as duplicate.
        ResetCallbacks();

        if (destroyNative)
            DestroyRewardedInstance(adUnitId);

        PreloadRewarded(adUnitId);
    }

    /// <inheritdoc/>
    public bool AbortRewardedShow(string adUnitId, string reason)
    {
        if (string.IsNullOrWhiteSpace(adUnitId))
            return false;

        if (TryDropDeferredRewardedShow(adUnitId, reason, out Action deferredFailed))
        {
            deferredFailed?.Invoke();
            return false;
        }

        if (_activeRewardedUnitId != adUnitId && _loadThenShowUnitId != adUnitId)
            return false;

        bool destroyNative = _rewardedShowIssued || _rewardedDisplayed;
        bool grant = _rewardEarned || HasQualifyingWatch();
        AppLog.Warn(
            "LevelPlay",
            $"Abort rewarded ({reason}): {adUnitId} grant={grant} sdkReward={_rewardEarned} displayed={_rewardedDisplayed} activity={_rewardedSessionActivity}");

        Action success = _pendingSuccess;
        Action failed = _pendingFailed;
        ResetCallbacks();

        if (destroyNative)
            DestroyRewardedInstance(adUnitId);

        if (grant)
        {
            AppLog.Info("LevelPlay", $"Rewarded granted (abort-after-close:{reason}) unit={adUnitId}");
            success?.Invoke();
        }
        else
            failed?.Invoke();

        PreloadRewarded(adUnitId);
        return grant;
    }

    /// <inheritdoc/>
    public bool AbortInterstitialShow(string adUnitId, string reason)
    {
        if (string.IsNullOrWhiteSpace(adUnitId))
            return false;

        if (TryDropDeferredInterstitialShow(adUnitId, reason, out Action deferredFailed))
        {
            deferredFailed?.Invoke();
            return true;
        }

        if (_activeInterstitialUnitId != adUnitId && _interstitialLoadThenShowUnitId != adUnitId)
            return false;

        bool destroyNative = _interstitialShowIssued || _interstitialDisplayed;
        AppLog.Warn(
            "LevelPlay",
            $"Abort interstitial ({reason}): {adUnitId} issued={_interstitialShowIssued} displayed={_interstitialDisplayed}");

        Action failed = _pendingInterstitialFailed;
        ResetInterstitialCallbacks();

        if (destroyNative)
            DestroyInterstitialInstance(adUnitId);

        failed?.Invoke();

        if (_preloadInterstitialUnitIds.Contains(adUnitId))
            PreloadInterstitial(adUnitId);

        return true;
    }

    /// <inheritdoc/>
    public void ShowInterstitial(string placementId, Action onClosed = null, Action onFailed = null)
    {
        if (ShouldBypassAsSuccess())
        {
            AppLog.Info("LevelPlay", "Interstitial bypassed (ShouldBypassAsSuccess).");
            onClosed?.Invoke();
            return;
        }

        if (!NetworkStatus.IsOnline)
        {
            AppLog.Warn("LevelPlay", "ShowInterstitial skipped — offline / soft-offline.");
            onFailed?.Invoke();
            return;
        }

        if (_initState == InitState.NotStarted)
            Initialize();

        if (_initState == InitState.AwaitingPrivacy || _initState == InitState.InProgress)
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
            DeferredInterstitialShowTimeoutAsync(placementId, ++_deferredInterstitialGeneration);
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
            AppLog.Warn(
                "LevelPlay",
                $"Interstitial already in progress ({_activeInterstitialUnitId}) — rejecting '{placementId}'.");
            onFailed?.Invoke();
            return;
        }

        string adUnitId = placementId;
        LevelPlayInterstitialAd ad = GetOrCreateInterstitial(adUnitId);

        _pendingInterstitialClosed = onClosed;
        _pendingInterstitialFailed = onFailed;
        _activeInterstitialUnitId = adUnitId;
        _interstitialShowIssued = false;
        _interstitialDisplayed = false;
        LastInterstitialNetwork = null;

        if (ad.IsAdReady())
        {
            IssueInterstitialShow(ad, adUnitId);
        }
        else
        {
            BeginInterstitialLoadThenShow(adUnitId);
            if (_interstitialLoadInFlight.Add(adUnitId))
            {
                AppLog.Info("LevelPlay", $"ShowInterstitial not ready — load then show: {adUnitId}");
                ad.LoadAd();
            }
            else
            {
                AppLog.Info("LevelPlay", $"ShowInterstitial awaiting in-flight preload: {adUnitId}");
            }
        }
    }

    void OnInitSuccess(LevelPlayConfiguration config)
    {
        _initState = InitState.Succeeded;
        AppLog.Info("LevelPlay", $"Initialized. {config}");
        FlushDeferredShows();
        FlushPendingPreloads();
    }

    void OnInitFailed(LevelPlayInitError error)
    {
        _initState = InitState.Failed;
        AppLog.Error("LevelPlay", $"Init failed: {error}");

        PendingRewardedShow deferredRewarded = _deferredRewardedShow;
        _deferredRewardedShow = default;
        _deferredRewardedGeneration++;
        deferredRewarded.OnFailed?.Invoke();

        PendingInterstitialShow deferredInterstitial = _deferredInterstitialShow;
        _deferredInterstitialShow = default;
        _deferredInterstitialGeneration++;
        deferredInterstitial.OnFailed?.Invoke();

        _pendingPreloadUnitIds.Clear();
        _pendingPreloadInterstitialUnitIds.Clear();
    }

    void FlushDeferredShows()
    {
        PendingRewardedShow deferredRewarded = _deferredRewardedShow;
        if (!string.IsNullOrWhiteSpace(deferredRewarded.PlacementId))
        {
            _deferredRewardedShow = default;
            _deferredRewardedGeneration++;
            ShowRewardedAd(deferredRewarded.PlacementId, deferredRewarded.OnSuccess, deferredRewarded.OnFailed);
        }

        PendingInterstitialShow deferredInterstitial = _deferredInterstitialShow;
        if (!string.IsNullOrWhiteSpace(deferredInterstitial.PlacementId))
        {
            _deferredInterstitialShow = default;
            _deferredInterstitialGeneration++;
            ShowInterstitial(
                deferredInterstitial.PlacementId,
                deferredInterstitial.OnClosed,
                deferredInterstitial.OnFailed);
        }
    }

    /// <summary>
    /// Drops a queued show for this unit so a late <c>OnInitSuccess</c> cannot open a
    /// fullscreen after the caller already gave up.
    /// </summary>
    bool TryDropDeferredRewardedShow(string adUnitId, string reason, out Action onFailed)
    {
        onFailed = null;
        if (_deferredRewardedShow.PlacementId != adUnitId ||
            string.IsNullOrWhiteSpace(adUnitId))
            return false;

        onFailed = _deferredRewardedShow.OnFailed;
        _deferredRewardedShow = default;
        _deferredRewardedGeneration++;
        AppLog.Warn("LevelPlay", $"Deferred rewarded show dropped ({reason}): {adUnitId}");
        return true;
    }

    bool TryDropDeferredInterstitialShow(string adUnitId, string reason, out Action onFailed)
    {
        onFailed = null;
        if (_deferredInterstitialShow.PlacementId != adUnitId ||
            string.IsNullOrWhiteSpace(adUnitId))
            return false;

        onFailed = _deferredInterstitialShow.OnFailed;
        _deferredInterstitialShow = default;
        _deferredInterstitialGeneration++;
        AppLog.Warn("LevelPlay", $"Deferred interstitial show dropped ({reason}): {adUnitId}");
        return true;
    }

    async void DeferredRewardedShowTimeoutAsync(string adUnitId, int generation)
    {
        try
        {
            await Task.Delay(_deferredShowTimeoutMs);
        }
        catch (Exception)
        {
            return;
        }

        if (generation != _deferredRewardedGeneration)
            return;

        if (!TryDropDeferredRewardedShow(
                adUnitId,
                $"init-wait-timeout:{_deferredShowTimeoutMs}ms",
                out Action onFailed))
            return;

        onFailed?.Invoke();
    }

    async void DeferredInterstitialShowTimeoutAsync(string adUnitId, int generation)
    {
        try
        {
            await Task.Delay(_deferredShowTimeoutMs);
        }
        catch (Exception)
        {
            return;
        }

        if (generation != _deferredInterstitialGeneration)
            return;

        if (!TryDropDeferredInterstitialShow(
                adUnitId,
                $"init-wait-timeout:{_deferredShowTimeoutMs}ms",
                out Action onFailed))
            return;

        onFailed?.Invoke();
    }

    void FlushPendingPreloads()
    {
        if (_pendingPreloadUnitIds.Count > 0)
        {
            string[] pending = new string[_pendingPreloadUnitIds.Count];
            _pendingPreloadUnitIds.CopyTo(pending);
            _pendingPreloadUnitIds.Clear();

            for (int i = 0; i < pending.Length; i++)
                PreloadRewarded(pending[i]);
        }

        if (_pendingPreloadInterstitialUnitIds.Count == 0)
            return;

        string[] pendingInterstitial = new string[_pendingPreloadInterstitialUnitIds.Count];
        _pendingPreloadInterstitialUnitIds.CopyTo(pendingInterstitial);
        _pendingPreloadInterstitialUnitIds.Clear();

        for (int i = 0; i < pendingInterstitial.Length; i++)
            PreloadInterstitial(pendingInterstitial[i]);
    }

    LevelPlayRewardedAd GetOrCreateRewarded(string adUnitId)
    {
        if (_rewardedAds.TryGetValue(adUnitId, out LevelPlayRewardedAd existing))
            return existing;

        var ad = new LevelPlayRewardedAd(adUnitId);
        ad.OnAdLoaded += info => OnRewardedLoaded(adUnitId, info);
        ad.OnAdLoadFailed += err => OnRewardedLoadFailed(adUnitId, err);
        ad.OnAdDisplayed += info => OnRewardedDisplayed(adUnitId, info);
        ad.OnAdDisplayFailed += (_, err) => OnRewardedDisplayFailed(adUnitId, err);
        ad.OnAdClicked += _ => OnRewardedClicked(adUnitId);
        ad.OnAdRewarded += (info, _) => OnRewardEarned(adUnitId, info);
        ad.OnAdClosed += info => OnRewardedClosed(adUnitId, info);
        _rewardedAds[adUnitId] = ad;
        return ad;
    }

    void OnRewardedLoaded(string adUnitId, LevelPlayAdInfo adInfo)
    {
        _rewardedLoadInFlight.Remove(adUnitId);
        AppLog.Info("LevelPlay", $"Rewarded loaded: {adUnitId} network={adInfo?.AdNetwork}");
        if (adUnitId != _activeRewardedUnitId)
            return;

        ClearLoadThenShowWait();
        if (_rewardedAds.TryGetValue(adUnitId, out LevelPlayRewardedAd ad))
            IssueRewardedShow(ad, adUnitId);
    }

    void IssueRewardedShow(LevelPlayRewardedAd ad, string adUnitId)
    {
        if (_rewardedShowIssued)
        {
            AppLog.Warn("LevelPlay", $"ShowAd skipped — already issued: {adUnitId}");
            return;
        }

        _rewardedShowIssued = true;
        AppLog.Info("LevelPlay", $"ShowAd issued: {adUnitId}");
        ad.ShowAd();
    }

    void OnRewardedDisplayed(string adUnitId, LevelPlayAdInfo adInfo)
    {
        AppLog.Info("LevelPlay", $"Rewarded displayed: {adUnitId} network={adInfo?.AdNetwork}");
        if (adUnitId != _activeRewardedUnitId)
            return;

        _rewardedShowIssued = true;
        _rewardedDisplayed = true;
        _rewardedDisplayRealtimeStart = Time.realtimeSinceStartup;
        _rewardedSessionActivity++;
        _closeDeferredForStoreLeave = false;
        LastRewardedNetwork = adInfo?.AdNetwork;
    }

    void OnRewardedClicked(string adUnitId)
    {
        AppLog.Info("LevelPlay", $"Rewarded clicked: {adUnitId}");
        if (adUnitId != _activeRewardedUnitId)
            return;

        _rewardedHadClick = true;
        _rewardedSessionActivity++;
    }

    void OnRewardedLoadFailed(string adUnitId, LevelPlayAdError error)
    {
        _rewardedLoadInFlight.Remove(adUnitId);
        AppLog.Warn("LevelPlay", $"Rewarded load failed ({adUnitId}): {error}");

        if (adUnitId == _activeRewardedUnitId &&
            (_rewardedShowIssued || _rewardedDisplayed || IsLoadWhileShownError(error)))
        {
            ClearLoadThenShowWait();
            AppLog.Warn(
                "LevelPlay",
                $"Rewarded load failed during live show — keeping session: {adUnitId} code={error?.ErrorCode}");
            return;
        }

        if (adUnitId == _activeRewardedUnitId)
        {
            ClearLoadThenShowWait();
            InvokeFailedAndReset();
            PreloadRewarded(adUnitId);
            return;
        }

        if (_preloadUnitIds.Contains(adUnitId))
            SchedulePreloadRetry(adUnitId);
    }

    void BeginLoadThenShow(string adUnitId)
    {
        _loadThenShowUnitId = adUnitId;
        int generation = ++_loadThenShowGeneration;
        LoadThenShowTimeoutAsync(adUnitId, generation);
    }

    void ClearLoadThenShowWait()
    {
        _loadThenShowUnitId = null;
        _loadThenShowGeneration++;
    }

    async void LoadThenShowTimeoutAsync(string adUnitId, int generation)
    {
        try
        {
            await Task.Delay(_loadThenShowTimeoutMs);
        }
        catch (Exception)
        {
            return;
        }

        if (generation != _loadThenShowGeneration)
            return;

        if (_loadThenShowUnitId != adUnitId || _activeRewardedUnitId != adUnitId)
            return;

        AppLog.Warn(
            "LevelPlay",
            $"Load-then-show timeout ({_loadThenShowTimeoutMs}ms): {adUnitId}");
        ClearLoadThenShowWait();
        InvokeFailedAndReset();
        PreloadRewarded(adUnitId);
    }

    void OnRewardedDisplayFailed(string adUnitId, LevelPlayAdError error)
    {
        AppLog.Warn("LevelPlay", $"Rewarded display failed ({adUnitId}): {error}");
        if (adUnitId == _activeRewardedUnitId)
            InvokeFailedAndReset();

        if (_preloadUnitIds.Contains(adUnitId))
            SchedulePreloadRetry(adUnitId);
    }

    void OnRewardEarned(string adUnitId, LevelPlayAdInfo adInfo)
    {
        AppLog.Info("LevelPlay", $"Rewarded earned: {adUnitId} network={adInfo?.AdNetwork}");
        if (adUnitId != _activeRewardedUnitId)
        {
            AppLog.Warn(
                "LevelPlay",
                $"OnAdRewarded ignored — unit mismatch active={_activeRewardedUnitId} got={adUnitId}");
            return;
        }

        if (_activeRewardedGeneration != _rewardedGeneration)
        {
            AppLog.Warn("LevelPlay", "OnAdRewarded ignored — stale generation (session already reset).");
            return;
        }

        if (_pendingSuccess == null && _pendingFailed == null)
        {
            AppLog.Warn("LevelPlay", "OnAdRewarded ignored — callbacks already cleared.");
            return;
        }

        if (_rewardEarned)
            return;

        _rewardEarned = true;
        AppLog.Info(
            "LevelPlay",
            $"Rewarded SDK callback ({adInfo?.AdNetwork}) — waiting for ad close before grant.");

        if (_rewardedClosed)
            DeliverGrantAndFinish(adUnitId, $"sdk-after-close:{adInfo?.AdNetwork}");
    }

    void DeliverGrant(string reason)
    {
        if (_pendingSuccess == null && _pendingFailed == null)
            return;

        Action callback = _pendingSuccess;
        _pendingSuccess = null;
        _pendingFailed = null;
        AppLog.Info("LevelPlay", $"Rewarded granted ({reason}) unit={_activeRewardedUnitId}");
        callback?.Invoke();
    }

    void DeliverGrantAndFinish(string adUnitId, string reason)
    {
        DeliverGrant(reason);
        FinishRewardedSession(adUnitId);
    }

    void OnRewardedClosed(string adUnitId, LevelPlayAdInfo adInfo)
    {
        float watchedMs = _rewardedDisplayed && _rewardedDisplayRealtimeStart > 0f
            ? (Time.realtimeSinceStartup - _rewardedDisplayRealtimeStart) * 1000f
            : 0f;
        AppLog.Info(
            "LevelPlay",
            $"Rewarded closed: {adUnitId} network={adInfo?.AdNetwork} " +
            $"earned={_rewardEarned} watchedMs={watchedMs:0} backgrounded={IsUnityBackgrounded()}");

        if (adUnitId != _activeRewardedUnitId)
        {
            PreloadRewarded(adUnitId);
            return;
        }

        if (IsUnityBackgrounded() && _rewardedHadClick)
        {
            _closeDeferredForStoreLeave = true;
            _rewardedClosed = true;
            AppLog.Info(
                "LevelPlay",
                $"OnAdClosed while backgrounded after click — deferring grant/fail: {adUnitId}");
            return;
        }

        _closeDeferredForStoreLeave = false;
        ResolveRewardedClosed(adUnitId);
    }

    void ResolveRewardedClosed(string adUnitId)
    {
        _rewardedClosed = true;

        if (_rewardEarned)
        {
            DeliverGrantAndFinish(adUnitId, "sdk+close");
            return;
        }

        int generation = _rewardedGeneration;
        WaitForLateRewardThenResolveAsync(adUnitId, generation);
    }

    async void WaitForLateRewardThenResolveAsync(string adUnitId, int generation)
    {
        try
        {
            await Task.Delay(_lateRewardGraceMs);
        }
        catch
        {
            return;
        }

        if (generation != _rewardedGeneration)
            return;

        if (_rewardEarned)
        {
            DeliverGrantAndFinish(adUnitId, "sdk-late+close");
            return;
        }

        if (adUnitId != _activeRewardedUnitId && !_rewardedClosed)
            return;

        if (_rewardEarned || HasQualifyingWatch())
        {
            string reason = _rewardEarned ? "sdk-late+close" : "close-after-qualifying-watch";
            DeliverGrantAndFinish(adUnitId, reason);
            return;
        }

        float watchedMs = _rewardedDisplayed && _rewardedDisplayRealtimeStart > 0f
            ? (Time.realtimeSinceStartup - _rewardedDisplayRealtimeStart) * 1000f
            : 0f;
        AppLog.Info(
            "LevelPlay",
            $"Rewarded closed without SDK reward / qualifying watch ({watchedMs:0}ms < {_qualifyingWatchMs}ms) — fail: {adUnitId}");
        InvokeFailedAndReset();
        PreloadRewarded(adUnitId);
    }

    void FinishRewardedSession(string adUnitId)
    {
        ResetCallbacks();
        PreloadRewarded(adUnitId);
    }

    void SchedulePreloadRetry(string adUnitId)
    {
        if (!_preloadRetryInFlight.Add(adUnitId))
            return;

        RetryPreloadAsync(adUnitId);
    }

    async void RetryPreloadAsync(string adUnitId)
    {
        try
        {
            await Task.Delay(PreloadRetryDelayMs);
            if (_initState != InitState.Succeeded)
                return;

            if (_preloadInterstitialUnitIds.Contains(adUnitId))
            {
                if (_interstitials.TryGetValue(adUnitId, out LevelPlayInterstitialAd interstitial) &&
                    interstitial.IsAdReady())
                    return;

                if (IsInterstitialNativeUp(adUnitId))
                    return;

                AppLog.Info("LevelPlay", $"Retry preload interstitial: {adUnitId}");
                PreloadInterstitial(adUnitId);
                return;
            }

            if (_rewardedAds.TryGetValue(adUnitId, out LevelPlayRewardedAd ad) && ad.IsAdReady())
                return;

            if (IsRewardedNativeUp(adUnitId))
                return;

            AppLog.Info("LevelPlay", $"Retry preload rewarded: {adUnitId}");
            PreloadRewarded(adUnitId);
        }
        finally
        {
            _preloadRetryInFlight.Remove(adUnitId);
        }
    }

    LevelPlayInterstitialAd GetOrCreateInterstitial(string adUnitId)
    {
        if (_interstitials.TryGetValue(adUnitId, out LevelPlayInterstitialAd existing))
            return existing;

        var ad = new LevelPlayInterstitialAd(adUnitId);
        ad.OnAdLoaded += _ => OnInterstitialLoaded(adUnitId);
        ad.OnAdLoadFailed += err => OnInterstitialLoadFailed(adUnitId, err);
        ad.OnAdDisplayed += info => OnInterstitialDisplayed(adUnitId, info);
        ad.OnAdDisplayFailed += (_, err) => OnInterstitialDisplayFailed(adUnitId, err);
        ad.OnAdClosed += _ => OnInterstitialClosed(adUnitId);
        _interstitials[adUnitId] = ad;
        return ad;
    }

    void OnInterstitialLoaded(string adUnitId)
    {
        _interstitialLoadInFlight.Remove(adUnitId);
        AppLog.Info("LevelPlay", $"Interstitial loaded: {adUnitId}");
        if (adUnitId != _activeInterstitialUnitId)
            return;

        ClearInterstitialLoadThenShowWait();
        if (!_interstitials.TryGetValue(adUnitId, out LevelPlayInterstitialAd ad))
            return;

        IssueInterstitialShow(ad, adUnitId);
    }

    void IssueInterstitialShow(LevelPlayInterstitialAd ad, string adUnitId)
    {
        if (_interstitialShowIssued)
        {
            AppLog.Warn("LevelPlay", $"Interstitial ShowAd skipped — already issued: {adUnitId}");
            return;
        }

        ClearInterstitialLoadThenShowWait();
        _interstitialShowIssued = true;
        AppLog.Info("LevelPlay", $"Interstitial ShowAd issued: {adUnitId}");
        ad.ShowAd();
#if UNITY_EDITOR
        ScheduleEditorInterstitialFallbackClose(adUnitId);
#endif
    }

    void OnInterstitialDisplayed(string adUnitId, LevelPlayAdInfo adInfo)
    {
        AppLog.Info("LevelPlay", $"Interstitial displayed: {adUnitId} network={adInfo?.AdNetwork}");
        if (adUnitId != _activeInterstitialUnitId)
            return;

        _interstitialShowIssued = true;
        _interstitialDisplayed = true;
        LastInterstitialNetwork = adInfo?.AdNetwork;
        ClearInterstitialLoadThenShowWait();
    }

    void OnInterstitialLoadFailed(string adUnitId, LevelPlayAdError error)
    {
        _interstitialLoadInFlight.Remove(adUnitId);
        AppLog.Warn("LevelPlay", $"Interstitial load failed ({adUnitId}): {error}");

        bool liveShow = adUnitId == _activeInterstitialUnitId &&
            (_interstitialShowIssued || _interstitialDisplayed || IsLoadWhileShownError(error));
        if (liveShow)
        {
            ClearInterstitialLoadThenShowWait();
            AppLog.Warn(
                "LevelPlay",
                $"Interstitial load failed during live show — keeping session: {adUnitId} code={error?.ErrorCode}");
            return;
        }

        if (adUnitId == _activeInterstitialUnitId)
        {
            ClearInterstitialLoadThenShowWait();
            InvokeInterstitialFailedAndReset();
            if (_preloadInterstitialUnitIds.Contains(adUnitId))
                SchedulePreloadRetry(adUnitId);
            return;
        }

        if (_preloadInterstitialUnitIds.Contains(adUnitId))
            SchedulePreloadRetry(adUnitId);
    }

    void OnInterstitialDisplayFailed(string adUnitId, LevelPlayAdError error)
    {
        AppLog.Warn("LevelPlay", $"Interstitial display failed ({adUnitId}): {error}");
        if (adUnitId == _activeInterstitialUnitId)
        {
            ClearInterstitialLoadThenShowWait();
            InvokeInterstitialFailedAndReset();
        }

        if (_preloadInterstitialUnitIds.Contains(adUnitId))
            SchedulePreloadRetry(adUnitId);
    }

    void OnInterstitialClosed(string adUnitId)
    {
        AppLog.Info("LevelPlay", $"Interstitial closed adUnitId={adUnitId}, active={_activeInterstitialUnitId}");

        if (adUnitId == _activeInterstitialUnitId)
        {
            Action cb = _pendingInterstitialClosed;
            ResetInterstitialCallbacks();
            cb?.Invoke();
        }

        if (_preloadInterstitialUnitIds.Contains(adUnitId))
            PreloadInterstitial(adUnitId);
    }

    void BeginInterstitialLoadThenShow(string adUnitId)
    {
        _interstitialLoadThenShowUnitId = adUnitId;
        int generation = ++_interstitialLoadThenShowGeneration;
        InterstitialLoadThenShowTimeoutAsync(adUnitId, generation);
    }

    void ClearInterstitialLoadThenShowWait()
    {
        _interstitialLoadThenShowUnitId = null;
        _interstitialLoadThenShowGeneration++;
    }

    async void InterstitialLoadThenShowTimeoutAsync(string adUnitId, int generation)
    {
        try
        {
            await Task.Delay(_loadThenShowTimeoutMs);
        }
        catch (Exception)
        {
            return;
        }

        if (generation != _interstitialLoadThenShowGeneration)
            return;

        if (_interstitialLoadThenShowUnitId != adUnitId || _activeInterstitialUnitId != adUnitId)
            return;

        if (_interstitialShowIssued || _interstitialDisplayed)
            return;

        AppLog.Warn(
            "LevelPlay",
            $"Interstitial load-then-show timeout ({_loadThenShowTimeoutMs}ms): {adUnitId}");
        ClearInterstitialLoadThenShowWait();
        InvokeInterstitialFailedAndReset();
        if (_preloadInterstitialUnitIds.Contains(adUnitId))
            SchedulePreloadRetry(adUnitId);
    }

    void ResetCallbacks()
    {
        ClearLoadThenShowWait();
        _pendingSuccess = null;
        _pendingFailed = null;
        if (!string.IsNullOrEmpty(_activeRewardedUnitId))
            _rewardedLoadInFlight.Remove(_activeRewardedUnitId);
        _activeRewardedUnitId = null;
        _rewardEarned = false;
        _rewardedClosed = false;
        _rewardedShowIssued = false;
        _rewardedDisplayed = false;
        _rewardedDisplayRealtimeStart = 0f;
        _rewardedSessionActivity = 0;
        _closeDeferredForStoreLeave = false;
        _rewardedHadClick = false;
        _rewardedGeneration++;
    }

    void DestroyRewardedInstance(string adUnitId)
    {
        if (!_rewardedAds.TryGetValue(adUnitId, out LevelPlayRewardedAd ad))
            return;

        _rewardedAds.Remove(adUnitId);
        try
        {
            ad.DestroyAd();
        }
        catch (Exception ex)
        {
            AppLog.Warn("LevelPlay", $"DestroyAd failed ({adUnitId}): {ex.Message}");
        }
    }

    void DestroyInterstitialInstance(string adUnitId)
    {
        if (!_interstitials.TryGetValue(adUnitId, out LevelPlayInterstitialAd ad))
            return;

        _interstitials.Remove(adUnitId);
        try
        {
            ad.DestroyAd();
        }
        catch (Exception ex)
        {
            AppLog.Warn("LevelPlay", $"Interstitial DestroyAd failed ({adUnitId}): {ex.Message}");
        }
    }

    void InvokeFailedAndReset()
    {
        Action callback = _pendingFailed;
        ResetCallbacks();
        callback?.Invoke();
    }

    void InvokeInterstitialFailedAndReset()
    {
        Action callback = _pendingInterstitialFailed;
        ResetInterstitialCallbacks();
        callback?.Invoke();
    }

    void ResetInterstitialCallbacks()
    {
        ClearInterstitialLoadThenShowWait();
        if (!string.IsNullOrEmpty(_activeInterstitialUnitId))
            _interstitialLoadInFlight.Remove(_activeInterstitialUnitId);

        _pendingInterstitialClosed = null;
        _pendingInterstitialFailed = null;
        _activeInterstitialUnitId = null;
        _interstitialShowIssued = false;
        _interstitialDisplayed = false;
    }

#if UNITY_EDITOR
    async void ScheduleEditorInterstitialFallbackClose(string adUnitId)
    {
        const int fallbackMs = 5000;
        await Task.Delay(fallbackMs);
        if (adUnitId != _activeInterstitialUnitId)
            return;

        AppLog.Warn("LevelPlay", $"Editor interstitial auto-closed after {fallbackMs}ms " +
            $"(mock OnAdClosed fallback): {adUnitId}");

        DestroyInterstitialInstance(adUnitId);
        OnInterstitialClosed(adUnitId);
    }
#endif
}
