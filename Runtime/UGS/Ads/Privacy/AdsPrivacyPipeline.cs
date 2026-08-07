using System.Threading;
using System.Threading.Tasks;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>
    /// Shared ads privacy flow: ATT (iOS) → UMP consent → LevelPlay COPPA / GDPR flags.
    /// Call before <c>LevelPlay.Init</c>. Age-gate UI stays in the game; pass <see cref="AdsPrivacyOptions.IsChildDirected"/>.
    /// </summary>
    public static class AdsPrivacyPipeline
    {
        static readonly object GateLock = new();
        static IAdsUmpConsentGate _umpGate = new NullAdsUmpConsentGate();
        static bool _completed;
        static bool _inFlight;
        static Task _inFlightTask = Task.CompletedTask;

        /// <summary>True after a successful <see cref="EnsureCompletedAsync"/> for this process.</summary>
        public static bool IsCompleted => _completed;

        /// <summary>Whether settings UI should show a Privacy Options entry point.</summary>
        public static bool IsPrivacyOptionsRequired
        {
            get
            {
                lock (GateLock)
                    return _umpGate.IsPrivacyOptionsRequired;
            }
        }

        /// <summary>
        /// Register a real UMP implementation (done automatically when Google Mobile Ads optional assembly loads).
        /// </summary>
        public static void RegisterUmpGate(IAdsUmpConsentGate gate)
        {
            if (gate == null)
                return;

            lock (GateLock)
            {
                _umpGate = gate;
            }

            Debug.Log("[AdsPrivacy] UMP consent gate registered.");
        }

        /// <summary>
        /// Idempotent: ATT → UMP → LevelPlay privacy flags.
        /// Fail-open on UMP errors so game start is not blocked forever.
        /// </summary>
        public static async Task EnsureCompletedAsync(
            AdsPrivacyOptions options,
            CancellationToken cancellationToken = default)
        {
            options ??= new AdsPrivacyOptions();

            if (_completed)
            {
                ApplyLevelPlayPrivacyFlags(options.IsChildDirected);
                return;
            }

            if (_inFlight)
            {
                await _inFlightTask.ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                ApplyLevelPlayPrivacyFlags(options.IsChildDirected);
                return;
            }

            _inFlight = true;
            var run = RunPipelineAsync(options, cancellationToken);
            _inFlightTask = run;
            try
            {
                await run.ConfigureAwait(true);
            }
            finally
            {
                _inFlight = false;
            }
        }

        /// <summary>Show UMP privacy options form (Profile / settings). Safe no-op when not required.</summary>
        public static Task ShowPrivacyOptionsAsync(CancellationToken cancellationToken = default)
        {
            IAdsUmpConsentGate gate;
            lock (GateLock)
                gate = _umpGate;

            return gate.ShowPrivacyOptionsAsync(cancellationToken);
        }

        static async Task RunPipelineAsync(AdsPrivacyOptions options, CancellationToken cancellationToken)
        {
            await AppTrackingTransparencyGate
                .RequestIfNeededAsync(options.IsChildDirected, cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            IAdsUmpConsentGate gate;
            lock (GateLock)
                gate = _umpGate;

            try
            {
                await gate.GatherConsentAsync(options, cancellationToken).ConfigureAwait(true);
            }
            catch (System.OperationCanceledException)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AdsPrivacy] UMP gather failed (fail-open): {ex.Message}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            ApplyLevelPlayPrivacyFlags(options.IsChildDirected);
            _completed = true;
            Debug.Log(
                $"[AdsPrivacy] Pipeline completed. COPPA={options.IsChildDirected}, " +
                $"privacyOptionsRequired={gate.IsPrivacyOptionsRequired}");
        }

        static void ApplyLevelPlayPrivacyFlags(bool isChildDirected)
        {
            LevelPlayPrivacySettings.SetCOPPA(isChildDirected);

            // GDPR: children are not given a consent form; treat as no personalized consent.
            // When UMP ran, the Google gate may also have set GDPR — re-apply COPPA last and
            // set GDPR false for children; for adults leave consent from UMP gate if it set it,
            // otherwise default false only when child.
            if (isChildDirected)
                LevelPlayPrivacySettings.SetGDPRConsent(false);

            Debug.Log($"[AdsPrivacy] LevelPlay COPPA={isChildDirected}");
        }
    }
}
