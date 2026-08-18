using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RamnD.GameServices.AppUpdate
{
    /// <summary>
    /// Store in-app update entry. Default is a no-op; Google Play Immediate registers
    /// from the optional <c>RamnD.GameServices.UGS.GooglePlayAppUpdate</c> assembly.
    /// iOS has no native equivalent — do not add a custom popup here.
    /// Call under the loading screen and await: if Play starts an Immediate update the
    /// process restarts, so the rest of bootstrap should not run first.
    /// </summary>
    public static class AppUpdatePipeline
    {
        static readonly object Gate = new object();
        static IAppUpdateService _service = new NullAppUpdateService();
        static Task _inFlight;

        public static void Register(IAppUpdateService service)
        {
            if (service == null)
                return;

            lock (Gate)
                _service = service;

            AppLog.Info("AppUpdate", $"Registered {service.GetType().FullName}.");
        }

        /// <summary>
        /// Fire after lobby is up. Coalesces concurrent calls. Fail-open on errors.
        /// </summary>
        public static Task PromptIfAvailableAsync(CancellationToken cancellationToken = default)
        {
            lock (Gate)
            {
                if (_inFlight != null && !_inFlight.IsCompleted)
                    return _inFlight;

                IAppUpdateService service = _service;
                _inFlight = PromptCoreAsync(service, cancellationToken);
                return _inFlight;
            }
        }

        static async Task PromptCoreAsync(IAppUpdateService service, CancellationToken cancellationToken)
        {
            try
            {
                await service.PromptIfAvailableAsync(cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("AppUpdate", $"Prompt failed (ignored): {ex.Message}");
            }
        }
    }
}
