using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_IOS && RAMND_HAS_IOS_ATT
using Unity.Advertisement.IosSupport;
#endif

namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>
    /// Apple App Tracking Transparency (ATT). Call before LevelPlay.Init.
    /// Requires <c>com.unity.ads.ios-support</c> in the consuming project (or this package).
    /// </summary>
    public static class AppTrackingTransparencyGate
    {
        /// <summary>
        /// On iPhonePlayer shows ATT when status is NOT_DETERMINED.
        /// Skips for child-directed users. Editor / Android / already decided — no-op.
        /// </summary>
        public static async Task RequestIfNeededAsync(
            bool isChildDirected,
            CancellationToken cancellationToken = default)
        {
#if UNITY_IOS && RAMND_HAS_IOS_ATT
            if (Application.platform != RuntimePlatform.IPhonePlayer)
                return;

            if (isChildDirected)
            {
                AppLog.Info("ATT", "Skipped — child-directed user (COPPA).");
                return;
            }

            ATTrackingStatusBinding.AuthorizationTrackingStatus status =
                ATTrackingStatusBinding.GetAuthorizationTrackingStatus();

            if (status != ATTrackingStatusBinding.AuthorizationTrackingStatus.NOT_DETERMINED)
            {
                AppLog.Info("ATT", $"Already decided: {status}");
                return;
            }

            AppLog.Info("ATT", "Requesting App Tracking Transparency…");

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            ATTrackingStatusBinding.RequestAuthorizationTracking(code =>
            {
                tcs.TrySetResult(code);
            });

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                int result = await tcs.Task.ConfigureAwait(true);
                AppLog.Info("ATT", $"Result: {(ATTrackingStatusBinding.AuthorizationTrackingStatus)result}");
            }
#else
            await Task.CompletedTask;
#endif
        }
    }
}
