using System;
using System.Reflection;

namespace RamnD.GameServices.Ads.Privacy.InMobiChoice
{
    /// <summary>Resolves InMobi Choice CMP types at runtime (manual Unity package import).</summary>
    static class InMobiChoiceReflection
    {
        static bool _probed;
        static bool _available;

        public static bool IsAvailable
        {
            get
            {
                EnsureProbed();
                return _available;
            }
        }

        /// <summary>Allow a fresh probe after assemblies finish loading (iOS IL2CPP / load order).</summary>
        public static void InvalidateProbe()
        {
            _probed = false;
            _available = false;
            ChoiceCmpType = null;
            ChoiceCmpManagerType = null;
            StartChoiceMethod = null;
            ForceDisplayUiMethod = null;
            GetTcStringMethod = null;
            DidLoadEvent = null;
            DidErrorEvent = null;
            DidReceiveIabVendorConsentEvent = null;
            UiStatusChangedEvent = null;
        }

        public static Type ChoiceCmpType { get; private set; }
        public static Type ChoiceCmpManagerType { get; private set; }

        public static MethodInfo StartChoiceMethod { get; private set; }
        public static MethodInfo ForceDisplayUiMethod { get; private set; }
        /// <summary>Optional — newer Choice packages may omit <c>GetTCString</c>; use IAB prefs.</summary>
        public static MethodInfo GetTcStringMethod { get; private set; }

        public static EventInfo DidLoadEvent { get; private set; }
        public static EventInfo DidErrorEvent { get; private set; }
        public static EventInfo DidReceiveIabVendorConsentEvent { get; private set; }
        /// <summary>Optional — <c>CMPUIStatusChangedEvent</c> (Visible / Dismissed / …).</summary>
        public static EventInfo UiStatusChangedEvent { get; private set; }

        static void EnsureProbed()
        {
            if (_probed)
                return;

            _probed = true;

            ChoiceCmpType = FindType("ChoiceCMP");
            ChoiceCmpManagerType = FindType("ChoiceCMPManager");
            if (ChoiceCmpType == null || ChoiceCmpManagerType == null)
            {
                AppLog.Warn(
                    "AdsPrivacy.InMobi",
                    "ChoiceCMP / ChoiceCMPManager types not found — is Assets/InMobi imported?");
                return;
            }

            StartChoiceMethod = FindStartChoiceMethod(ChoiceCmpType);
            ForceDisplayUiMethod = ChoiceCmpType.GetMethod(
                "ForceDisplayUI",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            GetTcStringMethod = ChoiceCmpType.GetMethod(
                "GetTCString",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            DidLoadEvent = ChoiceCmpManagerType.GetEvent(
                "CMPDidLoadEvent",
                BindingFlags.Public | BindingFlags.Static);
            DidErrorEvent = ChoiceCmpManagerType.GetEvent(
                "CMPDidErrorEvent",
                BindingFlags.Public | BindingFlags.Static);
            DidReceiveIabVendorConsentEvent = ChoiceCmpManagerType.GetEvent(
                "CMPDidReceiveIABVendorConsentEvent",
                BindingFlags.Public | BindingFlags.Static);
            UiStatusChangedEvent = ChoiceCmpManagerType.GetEvent(
                "CMPUIStatusChangedEvent",
                BindingFlags.Public | BindingFlags.Static);

            _available =
                StartChoiceMethod != null
                && ForceDisplayUiMethod != null
                && DidLoadEvent != null
                && DidErrorEvent != null;

            if (!_available)
            {
                AppLog.Warn(
                    "AdsPrivacy.InMobi",
                    "InMobi Choice types found but API incomplete " +
                    $"(StartChoice={StartChoiceMethod != null}, ForceDisplayUI={ForceDisplayUiMethod != null}, " +
                    $"DidLoad={DidLoadEvent != null}, DidError={DidErrorEvent != null}).");
            }
        }

        static MethodInfo FindStartChoiceMethod(Type choiceCmpType)
        {
            MethodInfo[] methods = choiceCmpType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            MethodInfo best = null;

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "StartChoice")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 1 || parameters[0].ParameterType != typeof(string))
                    continue;

                // Prefer (string, ChoiceStyle, bool) / (string, bool) over odd overloads.
                if (parameters.Length >= 2
                    && parameters[parameters.Length - 1].ParameterType == typeof(bool))
                    return method;

                if (best == null)
                    best = method;
            }

            return best;
        }

        static Type FindType(string typeName)
        {
            Type direct = Type.GetType($"{typeName}, Assembly-CSharp");
            if (direct != null)
                return direct;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type found = assemblies[i].GetType(typeName, throwOnError: false);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
