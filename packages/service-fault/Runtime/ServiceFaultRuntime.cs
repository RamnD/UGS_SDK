using UnityEngine;

/// <summary>
/// Ensures a <see cref="ServiceFaultHost"/> exists for the session.
/// </summary>
public static class ServiceFaultRuntime
{
    static ServiceFaultHost _host;
    static IServiceFaultCatalog _pendingCatalog;

    public static ServiceFaultHost Host => _host;

    public static void EnsureStarted(IServiceFaultCatalog catalog = null)
    {
        RegisterDiagnosticsProvider();

        if (catalog != null)
            _pendingCatalog = catalog;

        if (_host != null)
        {
            ApplyPendingToHost(_host);
            return;
        }

        var existing = Object.FindFirstObjectByType<ServiceFaultHost>();
        if (existing != null)
        {
            RegisterHost(existing);
            ApplyPendingToHost(existing);
            return;
        }

        var go = new GameObject("ServiceFaultHost");
        Object.DontDestroyOnLoad(go);
        var host = go.AddComponent<ServiceFaultHost>();
        RegisterHost(host);
        ApplyPendingToHost(host);
    }

    public static void EnsureStarted(DefaultServiceFaultCatalog catalog) =>
        EnsureStarted((IServiceFaultCatalog)catalog);

    static void ApplyPendingToHost(ServiceFaultHost host)
    {
        if (host == null)
            return;

        if (_pendingCatalog is DefaultServiceFaultCatalog assetCatalog)
            host.SetCatalog(assetCatalog);
        else if (_pendingCatalog != null)
            host.SetCatalog(_pendingCatalog);
    }

    static void RegisterDiagnosticsProvider()
    {
        AppDiagnostics.FaultSnapshotProvider = ServiceFaultPool.BuildDiagnosticsSnapshot;
    }

    internal static void RegisterHost(ServiceFaultHost host)
    {
        if (host == null)
            return;

        if (_host != null && _host != host)
            Debug.LogWarning("[ServiceFault] Multiple hosts — keeping the newest.", host);

        _host = host;
    }

    internal static void UnregisterHost(ServiceFaultHost host)
    {
        if (_host == host)
            _host = null;
    }
}
