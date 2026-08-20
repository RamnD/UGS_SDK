using UnityEngine;

namespace RamnD.GameServices.Ads.Privacy.InMobiChoice
{
    /// <summary>
    /// Registers <see cref="InMobiChoiceConsentGate"/> when the InMobi Choice package is present.
    /// Runs after Google UMP bootstrap so InMobi wins when both packages exist.
    /// </summary>
    static class InMobiChoiceConsentBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (!InMobiChoiceReflection.IsAvailable)
                return;

            AdsPrivacyPipeline.RegisterUmpGate(new InMobiChoiceConsentGate());
            AppLog.Info("AdsPrivacy.InMobi", "InMobi Choice consent gate registered.");
        }
    }
}
