using UnityEngine;

/// <summary>
/// Utility for checking network connectivity.
/// Used by SDK services to switch between online and offline modes.
/// </summary>
public static class NetworkStatus
{
    /// <summary>
    /// Set to true to simulate "no network" during debugging / offline-only UI.
    /// Takes precedence over <see cref="Application.internetReachability"/>.
    /// </summary>
    public static bool ForceOffline { get; set; }

    /// <summary>
    /// True if the device has an active connection (by default via
    /// <see cref="Application.internetReachability"/>) and <see cref="ForceOffline"/> is not set.
    /// Heuristic only — does not perform an actual server request.
    /// </summary>
    public static bool IsOnline =>
        !ForceOffline && Application.internetReachability != NetworkReachability.NotReachable;
}
