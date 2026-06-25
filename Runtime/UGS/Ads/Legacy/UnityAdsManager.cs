#if RAMND_LEGACY_UNITY_ADS
using System;
using UnityEngine;
using UnityEngine.Advertisements;

/// <summary>
/// <b>Legacy:</b> Реализация <see cref="IAdsManager"/> через Unity Ads SDK 4.x (без медиации).
/// <para>
/// Для новых проектов рекомендуется <see cref="LevelPlayAdsManager"/> — поддерживает медиацию
/// (Unity Ads + Meta + AppLovin и др.) и активно развивается.
/// </para>
/// <para>
/// Требует: Package Manager → <c>com.unity.ads</c> версии 4.x или новее.
/// Game ID настраивается в Project Settings → Monetization → Unity Ads.
/// </para>
/// Использование в bootstrap:
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

    // Коллбэки текущего rewarded показа — хранятся на время Show()
    private Action _pendingSuccess;
    private Action _pendingFailed;
    // ID текущего показа — нужен для разрешения коллизий LoadListener/ShowListener
    private string _activeShowPlacementId;

    /// <param name="androidGameId">Unity Ads Game ID для Android (Project Settings → Ads).</param>
    /// <param name="iosGameId">Unity Ads Game ID для iOS (Project Settings → Ads).</param>
    /// <param name="testMode">Включить тестовый режим. В продакшне — false.</param>
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

    /// <summary>Unity Ads SDK успешно инициализирован.</summary>
    public void OnInitializationComplete()
    {
        Debug.Log("[Ads] Unity Ads initialized.");
    }

    /// <summary>Ошибка инициализации SDK.</summary>
    public void OnInitializationFailed(UnityAdsInitializationError error, string message)
    {
        Debug.LogError($"[Ads] Init failed: {error} — {message}");
    }

    // ─── IUnityAdsLoadListener ────────────────────────────────────────────────

    /// <summary>Рекламный блок загружен — начинаем показ.</summary>
    public void OnUnityAdsAdLoaded(string placementId)
    {
        Debug.Log($"[Ads] Loaded: {placementId}");
        // Показываем только тот placement, который мы запросили (защита от коллизий)
        if (placementId == _activeShowPlacementId)
            Advertisement.Show(placementId, this);
    }

    /// <summary>Ошибка загрузки рекламного блока.</summary>
    public void OnUnityAdsFailedToLoad(string placementId, UnityAdsLoadError error, string message)
    {
        Debug.LogWarning($"[Ads] Failed to load {placementId}: {error} — {message}");
        if (placementId == _activeShowPlacementId)
            InvokeFailedAndReset();
    }

    // ─── IUnityAdsShowListener ────────────────────────────────────────────────

    /// <summary>Игрок начал смотреть ролик.</summary>
    public void OnUnityAdsShowStart(string placementId) { }

    /// <summary>Клик по рекламе.</summary>
    public void OnUnityAdsShowClick(string placementId) { }

    /// <summary>
    /// Показ завершён. <see cref="ShowResult.Finished"/> = игрок досмотрел → вызываем onSuccess.
    /// Любой другой результат (Skipped, Failed) → onFailed.
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

    /// <summary>Ошибка во время показа рекламы.</summary>
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
