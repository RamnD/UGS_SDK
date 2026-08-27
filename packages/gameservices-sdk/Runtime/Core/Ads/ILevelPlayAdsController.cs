/// <summary>
/// LevelPlay-specific ads surface on top of <see cref="IAdsManager"/>.
/// Mock / test / legacy Unity Ads managers do not implement this.
/// Cast <c>GameServicesLocator.Services.Ads</c> to this type after
/// <c>.WithAds(new LevelPlayAdsManager(...))</c>.
/// </summary>
public interface ILevelPlayAdsController : IAdsManager
{
    /// <summary>
    /// Starts <c>LevelPlay.Init</c>. Call after the ads privacy pipeline
    /// (ATT → CMP → COPPA) when init was deferred. No-op if init already
    /// started, succeeded, or failed.
    /// </summary>
    void BeginSdkInitialization();

    /// <summary>True after <c>LevelPlay.OnInitFailed</c>.</summary>
    bool IsSdkInitializationFailed { get; }

    /// <summary>Warm-load rewarded units so the next show can be <c>IsAdReady</c>.</summary>
    void PreloadRewardedUnits(params string[] adUnitIds);

    /// <summary>Warm-load interstitial units so the next show can be <c>IsAdReady</c>.</summary>
    void PreloadInterstitialUnits(params string[] adUnitIds);

    /// <summary>Re-load configured units that are not ready (e.g. after resume / reconnect).</summary>
    void EnsurePreloadedUnitsReady();

    bool IsRewardedReady(string adUnitId);

    bool IsInterstitialReady(string adUnitId);

    /// <summary>All non-empty ids in the list are <see cref="IsRewardedReady"/>.</summary>
    bool AreRewardedUnitsReady(params string[] adUnitIds);

    /// <summary>All non-empty ids in the list are <see cref="IsInterstitialReady"/>.</summary>
    bool AreInterstitialUnitsReady(params string[] adUnitIds);

    /// <summary>Native rewarded fullscreen is up — do not abort the session.</summary>
    bool IsRewardedShowInProgress(string adUnitId);

    /// <summary>Native interstitial fullscreen is up — do not LoadAd or abort the session.</summary>
    bool IsInterstitialShowInProgress(string adUnitId);

    /// <summary>LevelPlay network name from the last rewarded display, or null.</summary>
    string LastRewardedNetwork { get; }

    /// <summary>LevelPlay network name from the last interstitial display, or null.</summary>
    string LastInterstitialNetwork { get; }

    /// <summary>
    /// Cancel in-flight load-then-show (e.g. popup closed). Does not grant a reward.
    /// </summary>
    void CancelPendingRewardedShow(string adUnitId);

    /// <summary>
    /// Force-close a hung native session, then resolve grant.
    /// Reward is delivered only after the ad window is gone.
    /// </summary>
    /// <returns><c>true</c> if the player was granted after forced close.</returns>
    bool AbortRewardedShow(string adUnitId, string reason);

    /// <summary>
    /// Release a hung interstitial session (no close/display callback ever arrived)
    /// so later shows are not rejected as "already in progress". Invokes <c>onFailed</c>.
    /// </summary>
    /// <returns><c>true</c> if a session for this unit was released.</returns>
    bool AbortInterstitialShow(string adUnitId, string reason);

    /// <summary>
    /// Resume after Store / multitask during a live rewarded session.
    /// Finalizes a close that arrived while backgrounded.
    /// </summary>
    void NotifyRewardedForegroundResume();
}
