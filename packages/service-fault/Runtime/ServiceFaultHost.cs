using UnityEngine;

/// <summary>
/// Binds catalog and owns the dumb <see cref="ServiceFaultPopupBridge"/>.
/// UI presentation lives in game code (separate presenter).
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class ServiceFaultHost : MonoBehaviour
{
    [SerializeField] ServiceFaultCatalog _catalog;

    ServiceFaultPopupBridge _bridge;

    public ServiceFaultCatalog Catalog => _catalog;
    public ServiceFaultPopupBridge Bridge => _bridge;

    void Awake()
    {
        ServiceFaultRuntime.RegisterHost(this);
        if (_catalog != null)
            ServiceFaultPool.BindCatalog(_catalog);

        ServiceFaultPool.Clear(ServiceFaultDomain.Network, ServiceFaultKeys.NetworkOffline);

        EnsureBridge();
        EnsureNetworkWatcher();
    }

    void OnDestroy()
    {
        ServiceFaultRuntime.UnregisterHost(this);
    }

    public void SetCatalog(ServiceFaultCatalog catalog)
    {
        _catalog = catalog;
        if (_catalog != null)
            ServiceFaultPool.BindCatalog(_catalog);
    }

    public void SetCatalog(IServiceFaultCatalog catalog)
    {
        if (catalog != null)
            ServiceFaultPool.BindCatalog(catalog);
    }

    void EnsureBridge()
    {
        if (_bridge != null)
            return;

        _bridge = GetComponent<ServiceFaultPopupBridge>();
        if (_bridge == null)
            _bridge = gameObject.AddComponent<ServiceFaultPopupBridge>();
    }

    void EnsureNetworkWatcher()
    {
        if (GetComponent<NetworkConnectivityFaultWatcher>() == null)
            gameObject.AddComponent<NetworkConnectivityFaultWatcher>();
    }
}
