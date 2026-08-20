using UnityEngine;

namespace RamnD.GameServices.Ads.Privacy.InMobiChoice
{
    /// <summary>
    /// Registers <see cref="InMobiChoiceConsentGate"/> when the InMobi Choice package is present.
    /// Runs after Google UMP bootstrap so InMobi wins when both packages exist.
    /// </summary>
    static class InMobiChoiceConsentBootstrap
    {
        static bool _registered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void RegisterAfterAssemblies() => TryRegister("AfterAssembliesLoaded");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterBeforeScene() => TryRegister("BeforeSceneLoad");

        static void TryRegister(string phase)
        {
            if (_registered)
                return;

            InMobiChoiceReflection.InvalidateProbe();
            if (!InMobiChoiceReflection.IsAvailable)
                return;

            AdsPrivacyPipeline.RegisterUmpGate(new InMobiChoiceConsentGate());
            _registered = true;
            AppLog.Info("AdsPrivacy.InMobi", $"InMobi Choice consent gate registered ({phase}).");
        }
    }
}
