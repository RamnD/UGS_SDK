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
                AppLog.Warn("AdsPrivacy", "Google Mobile Ads (UMP) is not available — consent form skipped. " +
                    "Add com.google.ads.mobile for EU consent.");
            }

            return Task.CompletedTask;
        }

        public Task ShowPrivacyOptionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
