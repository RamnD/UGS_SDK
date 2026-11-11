using Unity.Services.LevelPlay;
using UnityEngine;
using UnityEngine.Scripting;

// Optional LevelPlay assembly is only entered via RuntimeInitializeOnLoadMethod
// unless the game constructs LevelPlayAdsManager. Without this, IL2CPP drops the
// assembly and COPPA / GDPR flags never reach the SDK.
[assembly: AlwaysLinkAssembly]

namespace RamnD.GameServices.Ads.Privacy
{
    /// <summary>
    /// Applies COPPA / GDPR on the LevelPlay SDK. Registered only when
    /// <c>com.unity.services.levelplay</c> is installed in the game.
    /// </summary>
    sealed class LevelPlaySdkPrivacyBridge : IAdsSdkPrivacyBridge
    {
        public bool IsActive => true;

        public void ApplyCoppa(bool isChildDirected)
        {
            LevelPlayPrivacySettings.SetCOPPA(isChildDirected);
        }

        public void SetGdprConsent(bool consent)
        {
#if RAMND_LEVELPLAY_GDPR_BOOL
            LevelPlayPrivacySettings.SetGDPRConsent(consent);
#else
            LevelPlay.SetConsent(consent);
#endif
        }
    }

    static class LevelPlaySdkPrivacyBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            AdsPrivacyPipeline.RegisterSdkPrivacyBridge(new LevelPlaySdkPrivacyBridge());
        }
    }
}
