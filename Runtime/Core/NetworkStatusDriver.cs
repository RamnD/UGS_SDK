using UnityEngine;

/// <summary>
/// Drives <see cref="NetworkStatus.Tick"/> so soft-offline cooldown expiry and OS
/// reachability flips publish <see cref="NetworkStatus.IsOnlineChanged"/>.
/// Created automatically via <see cref="RuntimeInitializeOnLoadMethodAttribute"/>.
/// </summary>
[DefaultExecutionOrder(-1000)]
sealed class NetworkStatusDriver : MonoBehaviour
{
    static NetworkStatusDriver _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null)
            return;

        var go = new GameObject("[RamnD.NetworkStatusDriver]");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        _instance = go.AddComponent<NetworkStatusDriver>();
    }

    void Update() => NetworkStatus.Tick();

    void OnApplicationPause(bool paused)
    {
        if (!paused)
            NetworkStatus.NotifyApplicationResumed();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
