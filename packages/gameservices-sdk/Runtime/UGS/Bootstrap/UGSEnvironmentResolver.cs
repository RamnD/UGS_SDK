using UnityEngine;

/// <summary>
/// Resolves the UGS environment name passed to <c>SetEnvironmentName</c>.
/// Tier symbols (<c>UGS_ENV_PRODUCTION</c>, <c>UGS_ENV_STAGING</c>, <c>UGS_ENV_DEVELOPMENT</c>)
/// stay the behavior switch for logs, ads debug, and the production analytics guard.
/// A custom dataset name comes from <see cref="UgsEnvironmentNameMap"/> in the game project.
/// The editor matches that list to the active Build Profile and writes
/// <see cref="UgsDatasetEnvironment"/> before play and before a player build.
/// </summary>
internal static class UGSEnvironmentResolver
{

    internal const string ProductionName = "production";
    internal const string StagingName = "staging";
    internal const string DevelopmentName = "development";

    static bool _warnedMultipleTiers;
    static bool _warnedTierNameMismatch;
    static string _cached;

    /// <summary>
    /// Same value as <see cref="Resolve"/> without the log line — for hot paths
    /// such as stamping the pending analytics queue.
    /// </summary>
    public static string Current => _cached ??= Resolve();

    public static string Resolve()
    {
        string tierName = ResolveTierName(out int tierCount);
        string customName = ReadDatasetOverride();
        string environmentName = SelectEnvironmentName(tierName, customName);

        if (tierCount > 1 && !_warnedMultipleTiers)
        {
            _warnedMultipleTiers = true;
            AppLog.Warn("SDK", "Multiple UGS environment tier symbols are defined. " +
                "Priority is UGS_ENV_PRODUCTION > UGS_ENV_STAGING > UGS_ENV_DEVELOPMENT. " +
                "Using deterministic priority to resolve the tier.");
        }

        if (!string.IsNullOrEmpty(customName) &&
            !EnvironmentNameMatchesTier(customName, tierName) &&
            !_warnedTierNameMismatch)
        {
            _warnedTierNameMismatch = true;
            AppLog.Warn("SDK",
                $"Custom UGS environment '{customName}' does not match tier '{tierName}'. " +
                "The dataset name is still used. Tier symbols still control logs, ads debug, " +
                "and the production analytics guard. " +
                $"Expected the custom name to equal '{tierName}' or start with '{tierName}-'.");
        }

        AppLog.Info("SDK", $"Resolved UGS environment: {environmentName} (tier {tierName})");
        _cached = environmentName;
        return environmentName;
    }

    /// <summary>
    /// Dataset string sent to Unity Services. A custom name replaces the tier default.
    /// </summary>
    internal static string SelectEnvironmentName(string tierName, string customName)
    {
        if (string.IsNullOrEmpty(customName))
            return tierName;

        return customName;
    }

    /// <summary>
    /// True when the dashboard name is the tier itself or a hypothesis name under that tier,
    /// such as a name that starts with <c>staging-</c> when the tier is <c>staging</c>.
    /// </summary>
    internal static bool EnvironmentNameMatchesTier(string environmentName, string tierName)
    {
        if (string.IsNullOrEmpty(environmentName) || string.IsNullOrEmpty(tierName))
            return false;

        if (string.Equals(environmentName, tierName, System.StringComparison.OrdinalIgnoreCase))
            return true;

        return environmentName.StartsWith(tierName + "-", System.StringComparison.OrdinalIgnoreCase);
    }

    static string ReadDatasetOverride()
    {
        UgsDatasetEnvironment asset = Resources.Load<UgsDatasetEnvironment>(UgsDatasetEnvironment.ResourceName);
        if (asset == null || string.IsNullOrWhiteSpace(asset.EnvironmentName))
            return null;

        return asset.EnvironmentName.Trim();
    }

    static string ResolveTierName(out int definedCount)
    {
        definedCount = 0;

#if UGS_ENV_PRODUCTION
        definedCount++;
#endif
#if UGS_ENV_STAGING
        definedCount++;
#endif
#if UGS_ENV_DEVELOPMENT
        definedCount++;
#endif

#if UGS_ENV_PRODUCTION
        return ProductionName;
#elif UGS_ENV_STAGING
        return StagingName;
#elif UGS_ENV_DEVELOPMENT
        return DevelopmentName;
#else
        return DevelopmentName;
#endif
    }
}
