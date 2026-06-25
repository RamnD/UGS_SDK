using System;
using System.Collections.Generic;
using Unity.Services.LevelPlay;
using UnityEngine;

/// <summary>
/// Реализация <see cref="IAdsManager"/> через Unity LevelPlay SDK 8.x (бывший IronSource).
/// Рекомендуемый путь для новых проектов — поддерживает медиацию (Unity Ads, Meta, AppLovin и др.).
/// <para>
/// Требует: Package Manager → <c>com.unity.services.levelplay</c> версии 8.x или новее.
/// App Key берётся из LevelPlay Dashboard (не из Project Settings).
/// </para>
/// <para>
/// Ad Unit IDs задаются строками (как в LevelPlay Dashboard). На стороне игры удобно держать enum и маппинг в строку.
/// </para>
/// Использование в bootstrap:
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

    // Кэшированные экземпляры рекламных блоков — создаются при первом обращении
    private readonly Dictionary<string, LevelPlayRewardedAd>    _rewardedAds    = new();
    private readonly Dictionary<string, LevelPlayInterstitialAd> _interstitials  = new();

    // Состояние текущего rewarded показа (одновременно может быть только один)
    private Action _pendingSuccess;
    private Action _pendingFailed;
    private string _activeRewardedUnitId;

    /// <param name="appKey">App Key из LevelPlay Dashboard → Apps → ваше приложение.</param>
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

        // isAdManagerEnabled=false → режим Ad Units (рекомендуемый в SDK 8.x)
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
        // Показываем только если именно этот юнит был запрошен через ShowRewardedAd
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
        // Сохраняем коллбэк до сброса, т.к. OnAdClosed может прийти после
        var callback = _pendingSuccess;
        ResetCallbacks(); // _activeRewardedUnitId → null до OnAdClosed
        callback?.Invoke();
    }

    private void OnRewardedClosed(string adUnitId)
    {
        // Если OnAdRewarded ещё не сработал (пользователь пропустил) → вызываем onFailed
        if (adUnitId == _activeRewardedUnitId)
            InvokeFailedAndReset();

        // Предзагружаем следующий ролик сразу после закрытия
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
        ad.OnAdClosed        += _        => _interstitials[adUnitId].LoadAd(); // предзагрузка
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
