using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace RamnD.GameServices.Editor
{
    /// <summary>
    /// Warns when platform auth plugins are present as assemblies but not as UPM packages.
    /// asmdef <c>versionDefines</c> only fire for registered packages — Assets-only imports leave
    /// <c>RAMND_HAS_*</c> unset and ship stub SignIn/Link paths.
    /// </summary>
    [InitializeOnLoad]
    static class OptionalPlatformPluginsGuard
    {
        const string SessionKey = "RamnD.GameServices.OptionalPlatformPluginsGuard.Logged";

        static OptionalPlatformPluginsGuard()
        {
            if (SessionState.GetBool(SessionKey, false))
                return;

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += CheckOnce;
        }

        static void CheckOnce()
        {
            PackageInfo[] packages = PackageInfo.GetAllRegisteredPackages();
            WarnIfAssemblyWithoutPackage(
                assemblyName: "Google.Play.Games",
                packageName: "com.google.play.games",
                define: "RAMND_HAS_GOOGLE_PLAY_GAMES",
                packages);
            WarnIfAssemblyWithoutPackage(
                assemblyName: "Apple.GameKit",
                packageName: "com.apple.unityplugin.gamekit",
                define: "RAMND_HAS_APPLE_GAMEKIT",
                packages);
            WarnIfAssemblyWithoutPackage(
                assemblyName: "AppleAuth",
                packageName: "com.lupidan.apple-signin-unity",
                define: "RAMND_HAS_APPLE_SIGNIN",
                packages);
        }

        static void WarnIfAssemblyWithoutPackage(
            string assemblyName,
            string packageName,
            string define,
            PackageInfo[] packages)
        {
            bool hasAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal));
            if (!hasAssembly)
                return;

            bool hasPackage = packages.Any(p => string.Equals(p.name, packageName, StringComparison.Ordinal));
            if (hasPackage)
                return;

            Debug.LogWarning(
                $"[RamnD.GameServices] Assembly '{assemblyName}' is loaded, but UPM package '{packageName}' is not registered. " +
                $"asmdef versionDefines will not set {define}, so optional auth/platform code may compile as stubs. " +
                $"Install via UPM (Packages/{packageName} or git/tarball) or add {define} to Player Settings scripting defines. " +
                "See docs/auth.md.");
        }
    }
}
