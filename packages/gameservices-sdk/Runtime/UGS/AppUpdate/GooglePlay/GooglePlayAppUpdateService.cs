using System.Threading;
using System.Threading.Tasks;
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using Google.Play.AppUpdate;
using Google.Play.Common;
using UnityEngine;
#endif

namespace RamnD.GameServices.AppUpdate.GooglePlay
{
    /// <summary>
    /// Native Google Play Immediate in-app update. No-op in Editor and when Play
    /// reports no update (sideload / already current).
    /// </summary>
    public sealed class GooglePlayAppUpdateService : IAppUpdateService
    {
        const int InfoTimeoutMs = 15000;

        public Task PromptIfAvailableAsync(CancellationToken cancellationToken = default)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return PromptAndroidAsync(cancellationToken);
#else
            return Task.CompletedTask;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static async Task PromptAndroidAsync(CancellationToken cancellationToken)
        {
            var manager = new AppUpdateManager();
            PlayAsyncOperation<AppUpdateInfo, AppUpdateErrorCode> infoOp = manager.GetAppUpdateInfo();
            if (!await WaitUntilDoneAsync(() => infoOp.IsDone, InfoTimeoutMs, cancellationToken))
            {
                Debug.LogWarning("[AppUpdate] GetAppUpdateInfo timed out.");
                return;
            }

            if (!infoOp.IsSuccessful)
            {
                Debug.LogWarning($"[AppUpdate] GetAppUpdateInfo failed: {infoOp.Error}");
                return;
            }

            AppUpdateInfo info = infoOp.GetResult();
            UpdateAvailability availability = info.UpdateAvailability;
            Debug.Log(
                $"[AppUpdate] availability={availability} immediateAllowed=" +
                $"{info.IsUpdateTypeAllowed(AppUpdateType.Immediate)}");

            bool shouldStart =
                availability == UpdateAvailability.UpdateAvailable
                || availability == UpdateAvailability.DeveloperTriggeredUpdateInProgress;
            if (!shouldStart)
                return;

            if (!info.IsUpdateTypeAllowed(AppUpdateType.Immediate))
            {
                Debug.Log("[AppUpdate] Immediate flow not allowed — skipping.");
                return;
            }

            AppUpdateOptions options = AppUpdateOptions.ImmediateAppUpdateOptions();
            AppUpdateRequest request = manager.StartUpdate(info, options);
            await WaitUntilDoneAsync(() => request.IsDone, timeoutMs: 0, cancellationToken);

            if (request.Error != AppUpdateErrorCode.NoError)
                Debug.LogWarning($"[AppUpdate] StartUpdate ended with {request.Error} status={request.Status}");
            else
                Debug.Log($"[AppUpdate] Immediate flow finished status={request.Status}");
        }

        static async Task<bool> WaitUntilDoneAsync(
            Func<bool> isDone,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            int waited = 0;
            while (!isDone())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timeoutMs > 0 && waited >= timeoutMs)
                    return false;

                await Task.Delay(50, cancellationToken);
                waited += 50;
            }

            return true;
        }
#endif
    }
}
