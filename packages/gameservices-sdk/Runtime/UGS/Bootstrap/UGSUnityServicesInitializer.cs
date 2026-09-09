using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Core.Environments.Internal;
using Unity.Services.Core.Internal;

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
        string expectedEnvironment = UGSEnvironmentResolver.Resolve();

        if (UnityServices.State != ServicesInitializationState.Uninitialized)
        {
            AssertActiveEnvironment(expectedEnvironment, alreadyInitialized: true);
            return;
        }

        Task init;
        lock (Gate)
        {
            if (UnityServices.State != ServicesInitializationState.Uninitialized)
            {
                init = null;
            }
            else if (_initTask != null && !_initTask.IsCompleted)
            {
                init = _initTask;
            }
            else
            {
                init = InitializeCoreAsync(expectedEnvironment, cancellationToken);
                _initTask = init;
            }
        }

        if (init == null)
        {
            AssertActiveEnvironment(expectedEnvironment, alreadyInitialized: true);
            return;
        }

        try
        {
            await init;
            cancellationToken.ThrowIfCancellationRequested();
            AssertActiveEnvironment(expectedEnvironment, alreadyInitialized: false);
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

    static async Task InitializeCoreAsync(string environmentName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // SetEnvironmentName is an extension method — do not use reflection on
        // InitializationOptions (that never finds it). Without this option, Core falls
        // back to the baked UnityServicesProjectConfiguration.json value, which is
        // whatever Environment Selector last wrote — almost always "production".
        var initOptions = new InitializationOptions().SetEnvironmentName(environmentName);

        AppLog.Info("SDK", $"Initializing Unity Services. Environment={environmentName}");
        await UnityServices.InitializeAsync(initOptions);
    }

    /// <summary>
    /// True when Core reports an active environment that differs from
    /// <see cref="UGSEnvironmentResolver"/>. False when they match or the active
    /// environment cannot be read yet — unread must not disable Analytics.
    /// </summary>
    public static bool TryConfirmEnvironmentMismatch(out string expected, out string actual)
    {
        expected = UGSEnvironmentResolver.Current;
        if (!TryGetActiveEnvironment(out actual))
        {
            actual = null;
            return false;
        }

        return !string.Equals(actual, expected, System.StringComparison.OrdinalIgnoreCase);
    }

    static void AssertActiveEnvironment(string expectedEnvironment, bool alreadyInitialized)
    {
        if (!TryGetActiveEnvironment(out string actualEnvironment))
        {
            AppLog.Warn(
                "SDK",
                alreadyInitialized
                    ? $"Unity Services already initialized; could not read active environment " +
                      $"(expected '{expectedEnvironment}')."
                    : $"Unity Services initialized but IEnvironments is unavailable " +
                      $"(expected '{expectedEnvironment}').");
            return;
        }

        if (string.Equals(actualEnvironment, expectedEnvironment, System.StringComparison.OrdinalIgnoreCase))
        {
            if (alreadyInitialized)
            {
                AppLog.Info(
                    "SDK",
                    $"Unity Services already initialized. Active environment='{actualEnvironment}'.");
            }
            else
            {
                AppLog.Info(
                    "SDK",
                    $"Unity Services active environment confirmed: '{actualEnvironment}'.");
            }

            return;
        }

        AppLog.Error(
            "SDK",
            alreadyInitialized
                ? $"Unity Services was already initialized in environment '{actualEnvironment}', " +
                  $"but this build expects '{expectedEnvironment}'. Analytics and other UGS calls " +
                  $"will hit the wrong environment — restart the player after switching Build Profile, " +
                  $"and ensure nothing calls UnityServices.InitializeAsync() before UGSServicesBuilder."
                : $"Unity Services initialized with environment '{actualEnvironment}', " +
                  $"but this build expects '{expectedEnvironment}'. " +
                  $"Check that a UGS environment named '{expectedEnvironment}' exists in the dashboard " +
                  $"and that InitializationOptions.SetEnvironmentName is applied.");
    }

    static bool TryGetActiveEnvironment(out string environmentName)
    {
        environmentName = null;
        try
        {
            if (CoreRegistry.Instance == null)
                return false;

            if (!CoreRegistry.Instance.TryGetServiceComponent(out IEnvironments environments) ||
                environments == null ||
                string.IsNullOrEmpty(environments.Current))
            {
                return false;
            }

            environmentName = environments.Current;
            return true;
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("SDK", $"Failed to read active UGS environment: {ex.Message}");
            return false;
        }
    }
}
