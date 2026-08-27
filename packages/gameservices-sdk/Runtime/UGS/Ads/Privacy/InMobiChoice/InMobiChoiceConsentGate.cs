using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RamnD.GameServices.Ads.Privacy.InMobiChoice
{
    /// <summary>InMobi Choice CMP via reflection (no compile-time dependency on their Unity package).</summary>
    public sealed class InMobiChoiceConsentGate : IAdsUmpConsentGate
    {
        const int LoadTimeoutMs = 30000;
        /// <summary>If CMP never reports Visible after load, treat as no form needed.</summary>
        const int UiAppearGraceMs = 2500;
        /// <summary>User can sit on the form; fail-open after this so bootstrap cannot hang forever.</summary>
        const int UiDismissTimeoutMs = 300000;

        const string CachedConsentPrefKey = "RamnD.AdsPrivacy.IabConsentCached";

        bool _started;
        bool _choiceSdkStarted;
        string _activePCode = string.Empty;

        public bool IsPrivacyOptionsRequired =>
            _started && !string.IsNullOrWhiteSpace(_activePCode);

        public async Task GatherConsentAsync(AdsPrivacyOptions options, CancellationToken cancellationToken)
        {
            options ??= new AdsPrivacyOptions();

            if (!InMobiChoiceReflection.IsAvailable)
            {
                AppLog.Warn(
                    "AdsPrivacy.InMobi",
                    "InMobi Choice CMP package is not in the project — consent form skipped.");
                return;
            }

            string pCode = NormalizePCode(options.InMobiChoicePCode);
            if (string.IsNullOrWhiteSpace(pCode))
            {
                AppLog.Warn(
                    "AdsPrivacy.InMobi",
                    "InMobiChoicePCode is empty — consent form skipped.");
                return;
            }

            if (options.IsChildDirected)
            {
                AppLog.Info(
                    "AdsPrivacy.InMobi",
                    "Child-directed — skipping InMobi Choice form.");
                AdsPrivacyPipeline.SetLevelPlayGdprConsent(false);
                return;
            }

            _activePCode = pCode;

            if (HasStoredIabConsent(out string cacheSource))
            {
                AppLog.Info(
                    "AdsPrivacy.InMobi",
                    $"Stored IAB consent ({cacheSource}) — skipping CMP UI.");
                _started = true;
                RememberIabConsentCache();
                ApplyLevelPlayGdprFromChoice();
                return;
            }

            bool loaded = await StartChoiceAndWaitForUiAsync(
                    pCode,
                    shouldDisplayIdfa: false,
                    waitForUi: true,
                    cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            _choiceSdkStarted = loaded || _choiceSdkStarted;
            _started = loaded || _started;
            if (HasStoredIabConsent(out _))
                RememberIabConsentCache();
            ApplyLevelPlayGdprFromChoice();
        }

        public async Task ShowPrivacyOptionsAsync(CancellationToken cancellationToken)
        {
            if (!IsPrivacyOptionsRequired || !InMobiChoiceReflection.IsAvailable)
            {
                AppLog.Info("AdsPrivacy.InMobi", "Privacy options not available — skip.");
                return;
            }

            try
            {
                if (!_choiceSdkStarted)
                {
                    bool loaded = await StartChoiceAndWaitForUiAsync(
                            _activePCode,
                            shouldDisplayIdfa: false,
                            waitForUi: false,
                            cancellationToken)
                        .ConfigureAwait(true);
                    cancellationToken.ThrowIfCancellationRequested();
                    _choiceSdkStarted = loaded;
                    if (!loaded)
                    {
                        AppLog.Warn("AdsPrivacy.InMobi", "Privacy options skip — StartChoice did not load.");
                        return;
                    }
                }

                await ForceDisplayUiAndWaitForDismissAsync(cancellationToken).ConfigureAwait(true);
                AppLog.Info("AdsPrivacy.InMobi", "ForceDisplayUI cycle finished.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warn("AdsPrivacy.InMobi", $"ForceDisplayUI failed: {ex.Message}");
            }

            ApplyLevelPlayGdprFromChoice();
        }

        static async Task<bool> StartChoiceAndWaitForUiAsync(
            string pCode,
            bool shouldDisplayIdfa,
            bool waitForUi,
            CancellationToken cancellationToken)
        {
            var loadTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var uiTracker = new CmpUiCycleTracker();
            Delegate loadHandler = null;
            Delegate errorHandler = null;
            Delegate consentHandler = null;
            Delegate uiHandler = null;

            try
            {
                loadHandler = CreateEventHandler(
                    InMobiChoiceReflection.DidLoadEvent,
                    payload =>
                    {
                        AppLog.Info("AdsPrivacy.InMobi", $"CMPDidLoad. {SummarizePing(payload)}");
                        if (PingSaysUiNotRequired(payload))
                            uiTracker.MarkDismiss();
                        loadTcs.TrySetResult(true);
                    });
                errorHandler = CreateEventHandler(
                    InMobiChoiceReflection.DidErrorEvent,
                    () =>
                    {
                        AppLog.Warn("AdsPrivacy.InMobi", "CMPDidError.");
                        loadTcs.TrySetResult(false);
                    });
                if (InMobiChoiceReflection.DidReceiveIabVendorConsentEvent != null)
                {
                    // Agree/Reject writes IAB prefs; native UI dismiss event is unreliable on iOS.
                    consentHandler = CreateEventHandler(
                        InMobiChoiceReflection.DidReceiveIabVendorConsentEvent,
                        () =>
                        {
                            RememberIabConsentCache();
                            ApplyLevelPlayGdprFromChoice();
                            AppLog.Info(
                                "AdsPrivacy.InMobi",
                                "CMPDidReceiveIABVendorConsent — treating consent UI as resolved.");
                            uiTracker.MarkDismiss();
                        });
                }

                uiHandler = TryCreateUiStatusHandler(uiTracker);

                AddHandler(InMobiChoiceReflection.DidLoadEvent, loadHandler);
                AddHandler(InMobiChoiceReflection.DidErrorEvent, errorHandler);
                if (consentHandler != null)
                    AddHandler(InMobiChoiceReflection.DidReceiveIabVendorConsentEvent, consentHandler);
                if (uiHandler != null)
                    AddHandler(InMobiChoiceReflection.UiStatusChangedEvent, uiHandler);

                object[] startArgs = BuildStartChoiceArgs(pCode, shouldDisplayIdfa);
                InMobiChoiceReflection.StartChoiceMethod.Invoke(obj: null, startArgs);
                AppLog.Info("AdsPrivacy.InMobi", $"StartChoice invoked (pCode len={pCode.Length}).");

                bool loaded;
                using (cancellationToken.Register(() => loadTcs.TrySetCanceled(cancellationToken)))
                {
                    Task completed = await Task.WhenAny(
                            loadTcs.Task,
                            Task.Delay(LoadTimeoutMs, cancellationToken))
                        .ConfigureAwait(true);

                    if (completed != loadTcs.Task)
                    {
                        AppLog.Warn(
                            "AdsPrivacy.InMobi",
                            $"StartChoice timed out after {LoadTimeoutMs}ms (fail-open).");
                        return false;
                    }

                    loaded = await loadTcs.Task.ConfigureAwait(true);
                }

                if (!loaded)
                    return false;

                if (!waitForUi)
                {
                    AppLog.Info("AdsPrivacy.InMobi", "StartChoice loaded — skipping CMP UI wait.");
                    return true;
                }

                await WaitForCmpUiCycleAsync(
                        uiTracker,
                        uiHandler != null || consentHandler != null,
                        cancellationToken)
                    .ConfigureAwait(true);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Warn("AdsPrivacy.InMobi", $"StartChoice failed (fail-open): {ex.Message}");
                return false;
            }
            finally
            {
                RemoveHandler(InMobiChoiceReflection.DidLoadEvent, loadHandler);
                RemoveHandler(InMobiChoiceReflection.DidErrorEvent, errorHandler);
                if (consentHandler != null)
                    RemoveHandler(InMobiChoiceReflection.DidReceiveIabVendorConsentEvent, consentHandler);
                if (uiHandler != null)
                    RemoveHandler(InMobiChoiceReflection.UiStatusChangedEvent, uiHandler);
            }
        }

        static async Task ForceDisplayUiAndWaitForDismissAsync(CancellationToken cancellationToken)
        {
            var uiTracker = new CmpUiCycleTracker();
            Delegate uiHandler = TryCreateUiStatusHandler(uiTracker);
            Delegate consentHandler = null;

            try
            {
                if (uiHandler != null)
                    AddHandler(InMobiChoiceReflection.UiStatusChangedEvent, uiHandler);

                if (InMobiChoiceReflection.DidReceiveIabVendorConsentEvent != null)
                {
                    consentHandler = CreateEventHandler(
                        InMobiChoiceReflection.DidReceiveIabVendorConsentEvent,
                        () =>
                        {
                            RememberIabConsentCache();
                            ApplyLevelPlayGdprFromChoice();
                            AppLog.Info(
                                "AdsPrivacy.InMobi",
                                "CMPDidReceiveIABVendorConsent — treating privacy options UI as resolved.");
                            uiTracker.MarkDismiss();
                        });
                    AddHandler(InMobiChoiceReflection.DidReceiveIabVendorConsentEvent, consentHandler);
                }

                InMobiChoiceReflection.ForceDisplayUiMethod.Invoke(obj: null, parameters: null);
                AppLog.Info("AdsPrivacy.InMobi", "ForceDisplayUI invoked.");

                await WaitForCmpUiCycleAsync(uiTracker, uiHandler != null || consentHandler != null, cancellationToken)
                    .ConfigureAwait(true);
            }
            finally
            {
                if (uiHandler != null)
                    RemoveHandler(InMobiChoiceReflection.UiStatusChangedEvent, uiHandler);
                if (consentHandler != null)
                    RemoveHandler(InMobiChoiceReflection.DidReceiveIabVendorConsentEvent, consentHandler);
            }
        }

        static async Task WaitForCmpUiCycleAsync(
            CmpUiCycleTracker tracker,
            bool hasCompletionSignals,
            CancellationToken cancellationToken)
        {
            if (!hasCompletionSignals || tracker == null)
            {
                AppLog.Warn(
                    "AdsPrivacy.InMobi",
                    "No CMP UI/consent completion signals — brief settle wait only.");
                await Task.Delay(800, cancellationToken).ConfigureAwait(true);
                return;
            }

            // Already dismissed / consent already applied.
            if (tracker.SawDismiss)
            {
                AppLog.Info("AdsPrivacy.InMobi", "CMP already resolved (dismiss or consent).");
                return;
            }

            if (!tracker.SawVisible)
            {
                Task appearOrResolved = await Task.WhenAny(
                        tracker.VisibleTask,
                        tracker.DismissTask,
                        Task.Delay(UiAppearGraceMs, cancellationToken))
                    .ConfigureAwait(true);

                cancellationToken.ThrowIfCancellationRequested();

                if (appearOrResolved == tracker.DismissTask || tracker.SawDismiss)
                {
                    AppLog.Info("AdsPrivacy.InMobi", "CMP resolved before Visible (consent or dismiss).");
                    return;
                }

                if (appearOrResolved != tracker.VisibleTask && !tracker.SawVisible)
                {
                    AppLog.Info(
                        "AdsPrivacy.InMobi",
                        $"CMP UI did not appear within {UiAppearGraceMs}ms — continuing.");
                    return;
                }
            }

            AppLog.Info("AdsPrivacy.InMobi", "CMP UI visible — waiting for dismiss or IAB consent.");

            if (tracker.SawDismiss)
                return;

            Task resolved = await Task.WhenAny(
                    tracker.DismissTask,
                    Task.Delay(UiDismissTimeoutMs, cancellationToken))
                .ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

            if (resolved != tracker.DismissTask && !tracker.SawDismiss)
            {
                AppLog.Warn(
                    "AdsPrivacy.InMobi",
                    $"CMP UI resolve timed out after {UiDismissTimeoutMs}ms (fail-open).");
                return;
            }

            AppLog.Info("AdsPrivacy.InMobi", "CMP UI resolved.");
        }

        static Delegate TryCreateUiStatusHandler(CmpUiCycleTracker tracker)
        {
            EventInfo uiEvent = InMobiChoiceReflection.UiStatusChangedEvent;
            if (uiEvent == null || tracker == null)
                return null;

            return CreateEventHandler(
                uiEvent,
                displayInfo =>
                {
                    string status = TryReadDisplayStatusName(displayInfo);
                    if (string.IsNullOrEmpty(status))
                        return;

                    if (string.Equals(status, "Visible", StringComparison.OrdinalIgnoreCase))
                    {
                        AppLog.Info("AdsPrivacy.InMobi", "CMP UI Visible.");
                        tracker.MarkVisible();
                        return;
                    }

                    if (string.Equals(status, "Hidden", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Dismissed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        AppLog.Info("AdsPrivacy.InMobi", $"CMP UI {status}.");
                        tracker.MarkDismiss();
                    }
                });
        }

        static string TryReadDisplayStatusName(object displayInfo)
        {
            if (displayInfo == null)
                return null;

            try
            {
                Type type = displayInfo.GetType();
                FieldInfo field = type.GetField(
                    "displayStatus",
                    BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    object value = field.GetValue(displayInfo);
                    return value?.ToString();
                }

                PropertyInfo prop = type.GetProperty(
                    "displayStatus",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    object value = prop.GetValue(displayInfo);
                    return value?.ToString();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("AdsPrivacy.InMobi", $"DisplayStatus read failed: {ex.Message}");
            }

            return null;
        }

        static object[] BuildStartChoiceArgs(string pCode, bool shouldDisplayIdfa)
        {
            ParameterInfo[] parameters = InMobiChoiceReflection.StartChoiceMethod.GetParameters();
            if (parameters.Length == 2)
                return new object[] { pCode, shouldDisplayIdfa };

            var args = new object[parameters.Length];
            args[0] = pCode;
            args[1] = null; // ChoiceStyle optional
            args[2] = shouldDisplayIdfa;
            return args;
        }

        static void ApplyLevelPlayGdprFromChoice()
        {
            bool consent = TryReadGdprConsentFromChoice();
            AdsPrivacyPipeline.SetLevelPlayGdprConsent(consent);
            AppLog.Info("AdsPrivacy.InMobi", $"LevelPlay GDPR consent={consent}.");
        }

        static bool TryReadGdprConsentFromChoice()
        {
            try
            {
                string tcString = null;
                if (InMobiChoiceReflection.GetTcStringMethod != null)
                {
                    tcString = InMobiChoiceReflection.GetTcStringMethod.Invoke(obj: null, parameters: null)
                        as string;
                }

                if (string.IsNullOrWhiteSpace(tcString))
                    tcString = ReadIabValue("IABTCF_TCString");

                if (string.IsNullOrWhiteSpace(tcString))
                {
                    // Outside GDPR / first launch before TC string — allow restricted ads (fail-open).
                    return true;
                }

                string purposeConsents = ReadIabValue("IABTCF_PurposeConsents");
                if (string.IsNullOrEmpty(purposeConsents))
                    return true;

                return purposeConsents[0] == '1';
            }
            catch (Exception ex)
            {
                AppLog.Warn("AdsPrivacy.InMobi", $"GDPR read failed (fail-open): {ex.Message}");
                return true;
            }
        }

        static bool HasStoredIabConsent(out string source)
        {
            if (PlayerPrefs.GetInt(CachedConsentPrefKey, 0) == 1)
            {
                source = "unity-flag";
                return true;
            }

            string gdprApplies = ReadIabValue("IABTCF_gdprApplies");
            if (gdprApplies == "0")
            {
                source = "IABTCF_gdprApplies=0";
                return true;
            }

            string tcString = ReadIabValue("IABTCF_TCString");
            if (!string.IsNullOrWhiteSpace(tcString))
            {
                source = $"IABTCF_TCString len={tcString.Length}";
                return true;
            }

            source = string.Empty;
            return false;
        }

        static void RememberIabConsentCache()
        {
            PlayerPrefs.SetInt(CachedConsentPrefKey, 1);
            PlayerPrefs.Save();
        }

        static string ReadIabValue(string key)
        {
            string fromUnity = PlayerPrefs.GetString(key, string.Empty);
            if (!string.IsNullOrWhiteSpace(fromUnity))
                return fromUnity;

            return ReadAndroidDefaultPrefsString(key);
        }

        static string ReadAndroidDefaultPrefsString(string key)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null)
                    return string.Empty;

                using AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext");
                using var prefMgr = new AndroidJavaClass("android.preference.PreferenceManager");
                using AndroidJavaObject prefs = prefMgr.CallStatic<AndroidJavaObject>(
                    "getDefaultSharedPreferences",
                    context);
                if (prefs == null || !prefs.Call<bool>("contains", key))
                    return string.Empty;

                try
                {
                    return prefs.Call<string>("getString", key, string.Empty) ?? string.Empty;
                }
                catch (Exception)
                {
                    return prefs.Call<int>("getInt", key, -1).ToString();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("AdsPrivacy.InMobi", $"Android IAB prefs read failed: {ex.Message}");
                return string.Empty;
            }
#else
            return string.Empty;
#endif
        }

        static bool PingSaysUiNotRequired(object payload)
        {
            if (payload == null)
                return false;

            try
            {
                Type type = payload.GetType();
                FieldInfo gdprField = type.GetField("gdprApplies", BindingFlags.Public | BindingFlags.Instance);
                if (gdprField != null && gdprField.FieldType == typeof(bool) && !(bool)gdprField.GetValue(payload))
                    return true;

                string displayStatus = type.GetField("displayStatus", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(payload)
                    ?.ToString();
                return !string.IsNullOrEmpty(displayStatus)
                    && (displayStatus.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                        || displayStatus.Equals("disabled", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                AppLog.Warn("AdsPrivacy.InMobi", $"PingResult read failed: {ex.Message}");
                return false;
            }
        }

        static string SummarizePing(object payload)
        {
            if (payload == null)
                return "ping=null";

            try
            {
                Type type = payload.GetType();
                object gdpr = type.GetField("gdprApplies", BindingFlags.Public | BindingFlags.Instance)?.GetValue(payload);
                object cmpStatus = type.GetField("cmpStatus", BindingFlags.Public | BindingFlags.Instance)?.GetValue(payload);
                object displayStatus = type.GetField("displayStatus", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(payload);
                return $"gdprApplies={gdpr} cmpStatus={cmpStatus} displayStatus={displayStatus}";
            }
            catch (Exception)
            {
                return "ping=unreadable";
            }
        }

        static string NormalizePCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string trimmed = raw.Trim();
            if (trimmed.StartsWith("p-", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring(2);

            return trimmed;
        }

        static Delegate CreateEventHandler(EventInfo eventInfo, Action callback)
        {
            if (eventInfo == null)
                throw new ArgumentNullException(nameof(eventInfo));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            Type handlerType = eventInfo.EventHandlerType;
            MethodInfo invoke = handlerType.GetMethod("Invoke");
            ParameterInfo[] parameters = invoke.GetParameters();

            var callbackConst = Expression.Constant(callback);
            var invokeCallback = Expression.Call(
                callbackConst,
                typeof(Action).GetMethod(nameof(Action.Invoke)));

            if (parameters.Length == 0)
                return Expression.Lambda(handlerType, invokeCallback).Compile();

            var paramExprs = new ParameterExpression[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                paramExprs[i] = Expression.Parameter(parameters[i].ParameterType, "arg" + i);

            return Expression.Lambda(handlerType, invokeCallback, paramExprs).Compile();
        }

        static Delegate CreateEventHandler(EventInfo eventInfo, Action<object> callback)
        {
            if (eventInfo == null)
                throw new ArgumentNullException(nameof(eventInfo));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            Type handlerType = eventInfo.EventHandlerType;
            MethodInfo invoke = handlerType.GetMethod("Invoke");
            ParameterInfo[] parameters = invoke.GetParameters();
            MethodInfo actionInvoke = typeof(Action<object>).GetMethod(nameof(Action<object>.Invoke));

            var callbackConst = Expression.Constant(callback);

            if (parameters.Length == 0)
            {
                var invokeNull = Expression.Call(
                    callbackConst,
                    actionInvoke,
                    Expression.Constant(null, typeof(object)));
                return Expression.Lambda(handlerType, invokeNull).Compile();
            }

            var paramExprs = new ParameterExpression[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                paramExprs[i] = Expression.Parameter(parameters[i].ParameterType, "arg" + i);

            Expression firstArg = Expression.Convert(paramExprs[0], typeof(object));

            var invokeCallback = Expression.Call(callbackConst, actionInvoke, firstArg);
            return Expression.Lambda(handlerType, invokeCallback, paramExprs).Compile();
        }

        static void AddHandler(EventInfo eventInfo, Delegate handler)
        {
            if (eventInfo == null || handler == null)
                return;

            eventInfo.AddEventHandler(target: null, handler);
        }

        static void RemoveHandler(EventInfo eventInfo, Delegate handler)
        {
            if (eventInfo == null || handler == null)
                return;

            eventInfo.RemoveEventHandler(target: null, handler);
        }

        sealed class CmpUiCycleTracker
        {
            readonly TaskCompletionSource<bool> _visible =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            readonly TaskCompletionSource<bool> _dismiss =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool SawVisible { get; private set; }
            public bool SawDismiss { get; private set; }
            public Task VisibleTask => _visible.Task;
            public Task DismissTask => _dismiss.Task;

            public void MarkVisible()
            {
                SawVisible = true;
                _visible.TrySetResult(true);
            }

            public void MarkDismiss()
            {
                SawDismiss = true;
                _dismiss.TrySetResult(true);
            }
        }
    }
}
