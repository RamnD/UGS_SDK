using System.Threading;
using System.Threading.Tasks;

namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>
    /// Google UMP consent step. Default is a no-op; Google Mobile Ads package registers a real gate.
    /// </summary>
    public interface IAdsUmpConsentGate
    {
        /// <summary>True when a privacy-options entry point should be shown in settings UI.</summary>
        bool IsPrivacyOptionsRequired { get; }

        /// <summary>
        /// Update consent info and show the form if required.
        /// Fail-open: errors are logged; the pipeline continues.
        /// </summary>
        Task GatherConsentAsync(AdsPrivacyOptions options, CancellationToken cancellationToken);

        /// <summary>Present the privacy options form (settings). No-op when not required / unavailable.</summary>
        Task ShowPrivacyOptionsAsync(CancellationToken cancellationToken);
    }
}
