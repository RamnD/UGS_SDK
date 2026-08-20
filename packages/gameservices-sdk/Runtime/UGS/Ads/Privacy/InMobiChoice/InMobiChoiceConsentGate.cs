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

        bool _started;
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
            bool loaded = await StartChoiceAndWaitForLoadAsync(
                    pCode,
                    shouldDisplayIdfa: false,
                    cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            _started = loaded || _started;
            ApplyLevelPlayGdprFromChoice();
        }

        public Task ShowPrivacyOptionsAsync(CancellationToken cancellationToken)
        {
            if (!IsPrivacyOptionsRequired || !InMobiChoiceReflection.IsAvailable)
            {
                AppLog.Info("AdsPrivacy.InMobi", "Privacy options not available — skip.");
                return Task.CompletedTask;
            }

            try
            {
                InMobiChoiceReflection.ForceDisplayUiMethod.Invoke(obj: null, parameters: null);
                AppLog.Info("AdsPrivacy.InMobi", "ForceDisplayUI invoked.");
            }
            catch (Exception ex)
            {
                AppLog.Warn("AdsPrivacy.InMobi", $"ForceDisplayUI failed: {ex.Message}");
            }

            ApplyLevelPlayGdprFromChoice();
            return Task.CompletedTask;
        }

        static async Task<bool> StartChoiceAndWaitForLoadAsync(
            string pCode,
            bool shouldDisplayIdfa,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Delegate loadHandler = null;
            Delegate errorHandler = null;
            Delegate consentHandler = null;

            try
            {
                loadHandler = CreateEventHandler(
                    InMobiChoiceReflection.DidLoadEvent,
                    () =>
                    {
                        AppLog.Info("AdsPrivacy.InMobi", "CMPDidLoad.");
                        tcs.TrySetResult(true);
                    });
                errorHandler = CreateEventHandler(
                    InMobiChoiceReflection.DidErrorEvent,
                    () =>
                    {
                        AppLog.Warn("AdsPrivacy.InMobi", "CMPDidError.");
                        tcs.TrySetResult(false);
                    });
                if (InMobiChoiceReflection.DidReceiveIabVendorConsentEvent != null)
                {
                    consentHandler = CreateEventHandler(
                        InMobiChoiceReflection.DidReceiveIabVendorConsentEvent,
                        () => ApplyLevelPlayGdprFromChoice());
                }

                AddHandler(InMobiChoiceReflection.DidLoadEvent, loadHandler);
                AddHandler(InMobiChoiceReflection.DidErrorEvent, errorHandler);
                if (consentHandler != null)
                    AddHandler(InMobiChoiceReflection.DidReceiveIabVendorConsentEvent, consentHandler);

                object[] startArgs = BuildStartChoiceArgs(pCode, shouldDisplayIdfa);
                InMobiChoiceReflection.StartChoiceMethod.Invoke(obj: null, startArgs);
                AppLog.Info("AdsPrivacy.InMobi", $"StartChoice invoked (pCode len={pCode.Length}).");

                using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
                {
                    Task completed = await Task.WhenAny(
                            tcs.Task,
                            Task.Delay(LoadTimeoutMs, cancellationToken))
                        .ConfigureAwait(true);

                    if (completed != tcs.Task)
                    {
                        AppLog.Warn(
                            "AdsPrivacy.InMobi",
                            $"StartChoice timed out after {LoadTimeoutMs}ms (fail-open).");
                        return false;
                    }

                    return await tcs.Task.ConfigureAwait(true);
                }
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
            }
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
                string tcString = InMobiChoiceReflection.GetTcStringMethod.Invoke(obj: null, parameters: null)
                    as string;
                if (string.IsNullOrWhiteSpace(tcString))
                {
                    // Outside GDPR / first launch before TC string — allow restricted ads (fail-open).
                    return true;
                }

                string purposeConsents = PlayerPrefs.GetString("IABTCF_PurposeConsents", string.Empty);
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

            Type handlerType = eventInfo.EventHandlerType;
            MethodInfo invoke = handlerType.GetMethod("Invoke");
            ParameterInfo[] parameters = invoke.GetParameters();

            if (parameters.Length == 0)
                return Delegate.CreateDelegate(handlerType, callback);

            var callbackConst = Expression.Constant(callback);
            var invokeCallback = Expression.Call(callbackConst, typeof(Action).GetMethod(nameof(Action.Invoke)));
            var paramExprs = new ParameterExpression[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                paramExprs[i] = Expression.Parameter(parameters[i].ParameterType, "arg" + i);

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
    }
}
