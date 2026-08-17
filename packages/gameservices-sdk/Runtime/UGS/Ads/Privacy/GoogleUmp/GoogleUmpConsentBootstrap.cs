using UnityEngine;
using UnityEngine.Scripting;

// Optional UMP assembly is only entered via RuntimeInitializeOnLoadMethod.
// Without this, IL2CPP managed stripping drops the whole assembly from player builds
// (Editor still compiles it; device falls back to NullAdsUmpConsentGate).
[assembly: AlwaysLinkAssembly]

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
