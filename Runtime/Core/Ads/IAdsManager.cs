using System;

/// <summary>
/// Сервис рекламы. Абстрагирован от конкретного SDK (AdMob, IronSource, Unity Ads…).
/// <para>
/// Placement ID строкой — см. конфиг вашего медиаторa (LevelPlay, Unity Ads legacy и т.д.).
/// На стороне игры удобно держать enum и маппинг в строку (например AdsKeysExtensions.ToPlacementId).
/// </para>
/// </summary>
public interface IAdsManager
{
    /// <summary>
    /// Инициализирует рекламный SDK. Вызывается один раз при старте приложения.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Показывает Rewarded рекламу (за вознаграждение).
    /// </summary>
    /// <param name="placementId">Идентификатор места показа (Ad Unit Id / placement).</param>
    /// <param name="onSuccess">Игрок досмотрел ролик до конца — выдайте награду.</param>
    /// <param name="onFailed">Ролик закрыт раньше или ошибка загрузки — не выдавайте награду.</param>
    void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null);

    /// <summary>
    /// Показывает полноэкранный Interstitial (без вознаграждения).
    /// </summary>
    void ShowInterstitial(string placementId);
}
