using System.Threading;
using System.Threading.Tasks;
using GoogleMobileAds.Ump.Api;
using RamnD.GameServices.Ads.Privacy;
using UnityEngine;

namespace RamnD.GameServices.Ads.Privacy.GoogleUmp
{
    /// <summary>UMP consent via Google Mobile Ads User Messaging Platform.</summary>
    public sealed class GoogleUmpConsentGate : IAdsUmpConsentGate
    {
        public bool IsPrivacyOptionsRequired =>
            ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;

        public async Task GatherConsentAsync(AdsPrivacyOptions options, CancellationToken cancellationToken)
        {
            options ??= new AdsPrivacyOptions();

            if (options.IsChildDirected)
            {
                AppLog.Info("AdsPrivacy.UMP", "Child-directed — skipping consent form (TagForUnderAgeOfConsent).");
                await UpdateConsentInfoAsync(options, tagForUnderAge: true, cancellationToken)
                    .ConfigureAwait(true);
                AdsPrivacyPipeline.SetLevelPlayGdprConsent(false);
                return;
            }

            await UpdateConsentInfoAsync(options, tagForUnderAge: false, cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            await LoadAndShowFormIfRequiredAsync(cancellationToken).ConfigureAwait(true);
            ApplyLevelPlayGdprFromUmp();
        }

        public async Task ShowPrivacyOptionsAsync(CancellationToken cancellationToken)
        {
            if (!IsPrivacyOptionsRequired)
            {
                AppLog.Info("AdsPrivacy.UMP", "Privacy options not required — skip.");
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConsentForm.ShowPrivacyOptionsForm(error =>
            {
                if (error != null)
                    AppLog.Warn("AdsPrivacy.UMP", $"Privacy options error: {error.Message}");
                else
                    ApplyLevelPlayGdprFromUmp();
                tcs.TrySetResult(true);
            });

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                await tcs.Task.ConfigureAwait(true);
        }

        static async Task UpdateConsentInfoAsync(
            AdsPrivacyOptions options,
            bool tagForUnderAge,
            CancellationToken cancellationToken)
        {
            var request = new ConsentRequestParameters
            {
                TagForUnderAgeOfConsent = tagForUnderAge,
            };

            if (options.DebugGeography != AdsPrivacyDebugGeography.Disabled
                || !string.IsNullOrWhiteSpace(options.DebugTestDeviceHashedId))
            {
                var debug = new ConsentDebugSettings
                {
                    DebugGeography = MapDebugGeography(options.DebugGeography),
                };

                if (!string.IsNullOrWhiteSpace(options.DebugTestDeviceHashedId))
                {
                    debug.TestDeviceHashedIds ??= new System.Collections.Generic.List<string>();
                    debug.TestDeviceHashedIds.Add(options.DebugTestDeviceHashedId.Trim());
                }

                request.ConsentDebugSettings = debug;
            }

            var tcs = new TaskCompletionSource<FormError>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConsentInformation.Update(request, error => tcs.TrySetResult(error));

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                FormError error = await tcs.Task.ConfigureAwait(true);
                if (error != null)
                {
                    AppLog.Warn("AdsPrivacy.UMP", $"ConsentInformation.Update failed: {error.Message}");
                    return;
                }
            }

            AppLog.Info("AdsPrivacy.UMP", $"Update ok. ConsentStatus={ConsentInformation.ConsentStatus}, " +
                $"CanRequestAds={ConsentInformation.CanRequestAds()}, " +
                $"PrivacyOptions={ConsentInformation.PrivacyOptionsRequirementStatus}");
        }

        static async Task LoadAndShowFormIfRequiredAsync(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<FormError>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConsentForm.LoadAndShowConsentFormIfRequired(error => tcs.TrySetResult(error));

            using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
            {
                FormError error = await tcs.Task.ConfigureAwait(true);
                if (error != null)
                    AppLog.Warn("AdsPrivacy.UMP", $"Consent form error: {error.Message}");
                else
                    AppLog.Info("AdsPrivacy.UMP", "Consent form step finished.");
            }
        }

        static void ApplyLevelPlayGdprFromUmp()
        {
            ConsentStatus status = ConsentInformation.ConsentStatus;
            bool consent =
                status == ConsentStatus.Obtained
                || status == ConsentStatus.NotRequired;

            AdsPrivacyPipeline.SetLevelPlayGdprConsent(consent);
            AppLog.Info("AdsPrivacy.UMP", $"LevelPlay GDPR consent={consent} (UMP status={status}, " +
                $"CanRequestAds={ConsentInformation.CanRequestAds()})");
        }

        static DebugGeography MapDebugGeography(AdsPrivacyDebugGeography geography) =>
            geography switch
            {
                AdsPrivacyDebugGeography.Eea => DebugGeography.EEA,
#if RAMND_GMA_GEOGRAPHY_OTHER
                AdsPrivacyDebugGeography.NotEea => DebugGeography.Other,
#else
                AdsPrivacyDebugGeography.NotEea => DebugGeography.NotEEA,
#endif
                _ => DebugGeography.Disabled,
            };
    }
}
