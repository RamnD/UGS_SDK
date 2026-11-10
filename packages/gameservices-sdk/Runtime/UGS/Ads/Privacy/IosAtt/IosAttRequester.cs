using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Scripting;
#if UNITY_IOS
using Unity.Advertisement.IosSupport;
using UnityEngine;
#endif

// Optional ATT assembly is entered only from RuntimeInitializeOnLoadMethod.
// Without AlwaysLinkAssembly, IL2CPP strips it and iOS never shows the prompt.
[assembly: AlwaysLinkAssembly]

namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>
    /// Apple App Tracking Transparency via <c>com.unity.ads.ios-support</c>.
    /// Compiled only when that package is installed in the game.
    /// </summary>
    sealed class IosAttRequester : IAppTrackingTransparencyRequester
    {
        public async Task RequestIfNeededAsync(
            bool isChildDirected,
            CancellationToken cancellationToken = default)
        {
#if UNITY_IOS
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

    static class IosAttBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            AppTrackingTransparencyGate.Register(new IosAttRequester());
        }
    }
}
