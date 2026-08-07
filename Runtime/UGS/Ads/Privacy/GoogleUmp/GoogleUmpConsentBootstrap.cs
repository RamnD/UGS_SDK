using UnityEngine;

namespace RamnD.GameServices.Ads.Privacy.GoogleUmp
{
    /// <summary>Registers <see cref="GoogleUmpConsentGate"/> when this optional assembly loads.</summary>
    static class GoogleUmpConsentBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            AdsPrivacyPipeline.RegisterUmpGate(new GoogleUmpConsentGate());
        }
    }
}
