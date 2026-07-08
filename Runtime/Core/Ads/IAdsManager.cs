using System;

/// <summary>
/// Ads service. Abstracted from the concrete SDK (AdMob, IronSource, Unity Ads…).
/// <para>
/// Placement ID as a string — see your mediator config (LevelPlay, legacy Unity Ads, etc.).
/// On the game side, an enum plus string mapping is convenient (e.g. AdsKeysExtensions.ToPlacementId).
/// </para>
/// </summary>
public interface IAdsManager
{
    /// <summary>
    /// Initializes the ads SDK. Call once at app start.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Shows rewarded video (for a reward).
    /// </summary>
    /// <param name="placementId">Placement identifier (Ad Unit Id / placement).</param>
    /// <param name="onSuccess">Player watched to the end — grant the reward.</param>
    /// <param name="onFailed">Closed early or load error — do not grant the reward.</param>
    void ShowRewardedAd(string placementId, Action onSuccess, Action onFailed = null);

    /// <summary>
    /// Shows a full-screen interstitial (no reward).
    /// </summary>
    /// <param name="onClosed">Called after the ad is closed/dismissed.</param>
    /// <param name="onFailed">Called on load/show failure.</param>
    void ShowInterstitial(string placementId, Action onClosed = null, Action onFailed = null);
}
