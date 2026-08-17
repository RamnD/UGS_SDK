using System;
using UnityEngine;
using UnityEngine.CrashReportHandler;

/// <summary>
/// Unity Diagnostics context for crash/exception reports.
/// Soft-fault metadata is optional via <see cref="FaultSnapshotProvider"/>.
/// </summary>
public static class AppDiagnostics
{
    const int LogBufferSize = 30;

    const string KeyEnv = "env";
    const string KeyBuild = "build";
    const string KeyPlayerId = "ugs_player_id";
    const string KeyFaultTop = "fault_top";
    const string KeyFaults = "faults";

    static bool _configured;

    /// <summary>Build label written to crash metadata. Defaults to <see cref="DefaultBuildInfoProvider"/>.</summary>
    public static IBuildInfoProvider BuildInfoProvider { get; set; } = DefaultBuildInfoProvider.Instance;

    /// <summary>
    /// Optional fault snapshot for crash metadata.
    /// Return null when no faults are active.
    /// Tuple: (topFaultId, commaSeparatedDomains).
    /// </summary>
    public static Func<(string topFaultId, string faultDomainsCsv)?> FaultSnapshotProvider { get; set; }

    public static void Configure()
    {
        CrashReportHandler.enableCaptureExceptions = true;
        CrashReportHandler.logBufferSize = LogBufferSize;

        Set(KeyEnv, ResolveEnvironmentName());
        Set(KeyBuild, BuildInfoProvider?.Format() ?? "unknown");
        Set(KeyPlayerId, "anon");

        _configured = true;
        RefreshFaultSnapshot();
        AppLog.Info("Diagnostics", $"configured buffer={LogBufferSize} env={ResolveEnvironmentName()}");
    }

    public static void SetPlayerId(string playerId)
    {
        EnsureConfigured();
        string id = string.IsNullOrWhiteSpace(playerId) || playerId == "unknown"
            ? "anon"
            : playerId.Trim();
        Set(KeyPlayerId, id);
    }

    public static void ClearPlayerId()
    {
        EnsureConfigured();
        Set(KeyPlayerId, "anon");
    }

    /// <summary>Sync <c>fault_top</c> / <c>faults</c> from <see cref="FaultSnapshotProvider"/>.</summary>
    public static void RefreshFaultSnapshot()
    {
        EnsureConfigured();

        (string topFaultId, string faultDomainsCsv)? snapshot = FaultSnapshotProvider?.Invoke();
        if (snapshot == null)
        {
            Set(KeyFaultTop, string.Empty);
            Set(KeyFaults, string.Empty);
            return;
        }

        Set(KeyFaultTop, snapshot.Value.topFaultId ?? string.Empty);
        Set(KeyFaults, snapshot.Value.faultDomainsCsv ?? string.Empty);
    }

    static void EnsureConfigured()
    {
        if (_configured)
            return;
        Configure();
    }

    static void Set(string key, string value) =>
        CrashReportHandler.SetUserMetadata(key, value ?? string.Empty);

    public static string ResolveEnvironmentName()
    {
#if UGS_ENV_PRODUCTION
        return "production";
#elif UGS_ENV_STAGING
        return "staging";
#elif UGS_ENV_DEVELOPMENT
        return "development";
#else
        return "development";
#endif
    }
}
