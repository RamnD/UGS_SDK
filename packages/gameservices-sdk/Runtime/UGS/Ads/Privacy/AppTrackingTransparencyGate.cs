using System.Threading;
using System.Threading.Tasks;

namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>
    /// iOS App Tracking Transparency prompt. Implemented by
    /// <c>RamnD.GameServices.UGS.IosAtt</c> when the game installs
    /// <c>com.unity.ads.ios-support</c>. Otherwise this is a no-op.
    /// </summary>
    public interface IAppTrackingTransparencyRequester
    {
        Task RequestIfNeededAsync(bool isChildDirected, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Apple App Tracking Transparency (ATT). Call before LevelPlay.Init.
    /// Requires <c>com.unity.ads.ios-support</c> in the consuming project.
    /// </summary>
    public static class AppTrackingTransparencyGate
    {
        static IAppTrackingTransparencyRequester _requester;

        /// <summary>Called by the optional iOS ATT assembly when that package is installed.</summary>
        public static void Register(IAppTrackingTransparencyRequester requester)
        {
            if (requester == null)
                return;

            _requester = requester;
            AppLog.Info("ATT", "ATT requester registered.");
        }

        /// <summary>
        /// On iPhonePlayer shows ATT when status is NOT_DETERMINED.
        /// Skips for child-directed users. Editor / Android / already decided — no-op.
        /// No-op when <c>com.unity.ads.ios-support</c> is not installed.
        /// </summary>
        public static Task RequestIfNeededAsync(
            bool isChildDirected,
            CancellationToken cancellationToken = default)
        {
            if (_requester == null)
                return Task.CompletedTask;

            return _requester.RequestIfNeededAsync(isChildDirected, cancellationToken);
        }
    }
}
