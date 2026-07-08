using UnityEngine;

/// <summary>
/// Resolves the UGS Environment name from build symbols.
/// Keep this logic centralized so Build Profiles can switch backend environments
/// without any code edits or branch-specific constants.
/// </summary>
internal static class UGSEnvironmentResolver
{
    public static string Resolve()
    {
        int definedEnvironmentCount = 0;

#if UGS_ENV_PRODUCTION
        definedEnvironmentCount++;
#endif
#if UGS_ENV_STAGING
        definedEnvironmentCount++;
#endif
#if UGS_ENV_DEVELOPMENT
        definedEnvironmentCount++;
#endif

        string environmentName;

#if UGS_ENV_PRODUCTION
        environmentName = "production";
#elif UGS_ENV_STAGING
        environmentName = "staging";
#elif UGS_ENV_DEVELOPMENT
        environmentName = "development";
#else
        environmentName = "development";
#endif

        if (definedEnvironmentCount > 1)
        {
            Debug.LogError(
                "[SDK] Multiple UGS environment symbols are defined. " +
                "Priority is UGS_ENV_PRODUCTION > UGS_ENV_STAGING > UGS_ENV_DEVELOPMENT.");
        }

        Debug.Log($"[SDK] Resolved UGS environment: {environmentName}");
        return environmentName;
    }
}
