using UnityEngine;
using UnityEngine.Scripting;

[assembly: AlwaysLinkAssembly]

namespace RamnD.GameServices.AppUpdate.GooglePlay
{
    /// <summary>Registers Play Immediate in-app updates when this optional assembly loads.</summary>
    static class GooglePlayAppUpdateBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            AppUpdatePipeline.Register(new GooglePlayAppUpdateService());
        }
    }
}
