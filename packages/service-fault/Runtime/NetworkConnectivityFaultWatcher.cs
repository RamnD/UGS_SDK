using UnityEngine;

/// <summary>
/// Maps <see cref="NetworkStatus"/> online flips into sticky <c>Network:offline</c> ServiceFault.
/// </summary>
[DisallowMultipleComponent]
public sealed class NetworkConnectivityFaultWatcher : MonoBehaviour
{
    void OnEnable()
    {
        NetworkStatus.IsOnlineChanged += OnOnlineChanged;
        NetworkStatus.Tick();
        Apply(NetworkStatus.IsOnline, force: true);
    }

    void OnDisable()
    {
        NetworkStatus.IsOnlineChanged -= OnOnlineChanged;
    }

    void Update()
    {
        NetworkStatus.Tick();
    }

    void OnOnlineChanged(bool isOnline) => Apply(isOnline, force: false);

    static void Apply(bool isOnline, bool force)
    {
        if (isOnline)
        {
            ServiceFaultPool.ClearActiveOnReconnect();
            return;
        }

        ServiceFaultPool.Report(
            ServiceFaultDomain.Network,
            ServiceFaultKeys.NetworkOffline,
            force ? "STARTUP_OFFLINE" : "UNREACHABLE");
    }
}
