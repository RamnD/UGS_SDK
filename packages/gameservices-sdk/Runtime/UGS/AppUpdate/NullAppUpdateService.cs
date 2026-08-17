using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RamnD.GameServices.AppUpdate
{
    /// <summary>
    /// No-op until <see cref="AppUpdatePipeline.Register"/> gets a store adapter
    /// (Play In-App Updates optional assembly).
    /// </summary>
    public sealed class NullAppUpdateService : IAppUpdateService
    {
        bool _logged;

        public Task PromptIfAvailableAsync(CancellationToken cancellationToken = default)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_logged)
            {
                _logged = true;
                Debug.Log(
                    "[AppUpdate] Play In-App Updates not available — add com.google.play.appupdate " +
                    "for the native Google Play update flow.");
            }
#endif
            return Task.CompletedTask;
        }
    }
}
