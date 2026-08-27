using System;

/// <summary>
/// Game-owned hooks and timings for <see cref="LevelPlayAdsManager"/>.
/// Keep placement ids, analytics, and IAP no-ads logic in the game.
/// </summary>
public sealed class LevelPlayAdsOptions
{
    /// <summary>
    /// When true, <see cref="IAdsManager.Initialize"/> waits for
    /// <see cref="ILevelPlayAdsController.BeginSdkInitialization"/> instead of
    /// calling <c>LevelPlay.Init</c> immediately. Use after ATT / CMP / COPPA.
    /// Default false preserves 2.1.x bootstrap (init from <c>UGSServicesBuilder</c>).
    /// </summary>
    public bool DeferInitUntilPrivacy { get; set; }

    /// <summary>
    /// When true, rewarded show invokes <c>onSuccess</c> and interstitial show
    /// invokes <c>onClosed</c> without loading. Game owns no-ads entitlement.
    /// </summary>
    public Func<bool> ShouldBypassAsSuccess { get; set; }

    /// <summary>
    /// When true, treat Unity as backgrounded (store leave / multitask).
    /// Game can pass a pause/focus probe. Null falls back to <c>!Application.isFocused</c>.
    /// </summary>
    public Func<bool> IsUnityBackgrounded { get; set; }

    /// <summary>
    /// Wall-clock ms on screen after which a close without <c>OnAdRewarded</c>
    /// still grants. <c>0</c> disables (strict adapter reward only).
    /// Maze rewarded creatives are ~30s — pass <c>30000</c> there.
    /// </summary>
    public int QualifyingWatchMs { get; set; }

    /// <summary>
    /// Wait after <c>OnAdClosed</c> for a late <c>OnAdRewarded</c> before failing.
    /// </summary>
    public int LateRewardGraceMs { get; set; } = 5000;

    /// <summary>Max wait for LoadAd before failing a load-then-show attempt.</summary>
    public int LoadThenShowTimeoutMs { get; set; } = 10000;
}
