using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>No-op UMP gate used when Google Mobile Ads is not installed.</summary>
    public sealed class NullAdsUmpConsentGate : IAdsUmpConsentGate
    {
        bool _warnedMissing;

        public bool IsPrivacyOptionsRequired => false;

        public Task GatherConsentAsync(AdsPrivacyOptions options, CancellationToken cancellationToken)
        {
            if (!_warnedMissing)
            {
                _warnedMissing = true;
                AppLog.Warn("AdsPrivacy", "No CMP consent gate registered — consent form skipped. " +
                    "Add InMobi Choice (with InMobiChoicePCode) or com.google.ads.mobile (Google UMP).");
            }

            return Task.CompletedTask;
        }

        public Task ShowPrivacyOptionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
