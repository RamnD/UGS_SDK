using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Shared Unity Services initialization with the same environment resolution as
/// <see cref="UGSServicesBuilder"/> — used when Auth signs in without going through BuildAsync.
/// </summary>
internal static class UGSUnityServicesInitializer
{
    static readonly object Gate = new object();
    static Task _initTask;

    /// <summary>
    /// Ensures <see cref="UnityServices"/> is initialized with the resolved UGS environment.
    /// Concurrent callers share one in-flight init. No-op if already initialized.
    /// </summary>
    public static async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (UnityServices.State != ServicesInitializationState.Uninitialized)
            return;

        Task init;
        lock (Gate)
        {
            if (UnityServices.State != ServicesInitializationState.Uninitialized)
                return;

            if (_initTask != null && !_initTask.IsCompleted)
                init = _initTask;
            else
            {
                init = InitializeCoreAsync(cancellationToken);
                _initTask = init;
            }
        }

        try
        {
            await init;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (Gate)
            {
                if (ReferenceEquals(_initTask, init) && init.IsCompleted)
                    _initTask = null;
            }
        }
    }

    static async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string environmentName = UGSEnvironmentResolver.Resolve();
        var initOptions = new InitializationOptions();
        TrySetEnvironmentName(initOptions, environmentName);

        AppLog.Info("SDK", $"Initializing Unity Services. Environment={environmentName}");
        await UnityServices.InitializeAsync(initOptions);
    }

    static void TrySetEnvironmentName(InitializationOptions initOptions, string environmentName)
    {
        var t = initOptions.GetType();

        var setMethod = t.GetMethod(
            "SetEnvironmentName",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        if (setMethod != null)
        {
            setMethod.Invoke(initOptions, new object[] { environmentName });
            return;
        }

        var envProp =
            t.GetProperty(
                "EnvironmentName",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)
            ?? t.GetProperty(
                "environmentName",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

        if (envProp != null && envProp.CanWrite)
        {
            envProp.SetValue(initOptions, environmentName);
            return;
        }

        var setOptionMethod = t.GetMethod(
            "SetOption",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(string) },
            modifiers: null);

        if (setOptionMethod != null)
        {
            setOptionMethod.Invoke(
                initOptions,
                new object[] { "com.unity.services.core.environment-name", environmentName });
            AppLog.Info("SDK", $"Applied Unity Services environment via SetOption: {environmentName}");
            return;
        }

        AppLog.Warn("SDK", "Unity Services environment was not set via InitializationOptions API " +
            "(SetEnvironmentName/EnvironmentName/SetOption not found). Falling back to default Unity environment.");
    }
}
