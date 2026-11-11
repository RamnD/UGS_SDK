using System.Threading;
using System.Threading.Tasks;

namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>
    /// LevelPlay COPPA / GDPR target. The LevelPlay assembly registers an implementation
    /// when <c>com.unity.services.levelplay</c> is installed. Otherwise flags are skipped.
    /// </summary>
    public interface IAdsSdkPrivacyBridge
    {
        /// <summary>False for the built-in no-op used when LevelPlay is not installed.</summary>
        bool IsActive { get; }

        void ApplyCoppa(bool isChildDirected);

        void SetGdprConsent(bool consent);
    }

    /// <summary>
    /// Shared ads privacy flow: ATT (iOS) → UMP consent → LevelPlay COPPA / GDPR flags.
    /// Call before <c>LevelPlay.Init</c>. Age-gate UI stays in the game; pass <see cref="AdsPrivacyOptions.IsChildDirected"/>.
    /// Without LevelPlay, ATT and CMP still run and the SDK flags are skipped.
    /// </summary>
    public static class AdsPrivacyPipeline
    {
        static readonly object GateLock = new();
        static IAdsUmpConsentGate _umpGate = new NullAdsUmpConsentGate();
        static IAdsSdkPrivacyBridge _sdkPrivacy = NullAdsSdkPrivacyBridge.Instance;
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

            AppLog.Info("AdsPrivacy", "CMP consent gate registered.");
        }

        /// <summary>
        /// Register the ads SDK that receives COPPA / GDPR (LevelPlay, when that package is installed).
        /// </summary>
        public static void RegisterSdkPrivacyBridge(IAdsSdkPrivacyBridge bridge)
        {
            if (bridge == null)
                return;

            _sdkPrivacy = bridge;
            AppLog.Info("AdsPrivacy", $"SDK privacy bridge registered: {bridge.GetType().FullName}");
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

            AppLog.Info("AdsPrivacy", $"CMP gate: {gate.GetType().FullName}");

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
                AppLog.Warn("AdsPrivacy", $"UMP gather failed (fail-open): {ex.Message}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            ApplyLevelPlayPrivacyFlags(options.IsChildDirected);
            _completed = true;
            AppLog.Info("AdsPrivacy", $"Pipeline completed. COPPA={options.IsChildDirected}, " +
                $"privacyOptionsRequired={gate.IsPrivacyOptionsRequired}");
        }

        static void ApplyLevelPlayPrivacyFlags(bool isChildDirected)
        {
            if (!_sdkPrivacy.IsActive)
            {
                AppLog.Info("AdsPrivacy", "LevelPlay is not installed — COPPA/GDPR flags skipped.");
                return;
            }

            _sdkPrivacy.ApplyCoppa(isChildDirected);

            // GDPR: children are not given a consent form; treat as no personalized consent.
            // When UMP ran, the Google gate may also have set GDPR — re-apply COPPA last and
            // set GDPR false for children; for adults leave consent from UMP gate if it set it,
            // otherwise default false only when child.
            if (isChildDirected)
                SetLevelPlayGdprConsent(false);

            AppLog.Info("AdsPrivacy", $"LevelPlay COPPA={isChildDirected}");
        }

        /// <summary>
        /// GDPR flag for LevelPlay. 9.5+ uses <c>SetGDPRConsent(bool)</c>; 9.4.x uses global <c>LevelPlay.SetConsent</c>.
        /// No-op when LevelPlay is not installed.
        /// </summary>
        public static void SetLevelPlayGdprConsent(bool consent)
        {
            _sdkPrivacy.SetGdprConsent(consent);
        }

        sealed class NullAdsSdkPrivacyBridge : IAdsSdkPrivacyBridge
        {
            public static readonly NullAdsSdkPrivacyBridge Instance = new();

            public bool IsActive => false;

            public void ApplyCoppa(bool isChildDirected)
            {
            }

            public void SetGdprConsent(bool consent)
            {
            }
        }
    }
}
