using System;
using UnityEngine;

/// <summary>
/// Connectivity helper used by SDK services to choose online vs local-cache paths.
/// Combines Unity reachability, optional debug <see cref="ForceOffline"/>, and a soft
/// circuit breaker for "online but unusable" links (timeouts / DPI / packet loss).
/// </summary>
public static class NetworkStatus
{
    /// <summary>Consecutive recoverable failures within <see cref="FailureWindowSeconds"/> before soft-offline.</summary>
    public const int FailureThreshold = 3;

    /// <summary>Only failures inside this window count toward the breaker.</summary>
    public const float FailureWindowSeconds = 60f;

    /// <summary>Base soft-offline duration (seconds, realtime). Escalates on repeated trips.</summary>
    public const float CooldownSeconds = 20f;

    public const float MaxCooldownSeconds = 80f;

    static int _consecutiveFailures;
    static float _firstFailureRealtime;
    static float _cooldownUntilRealtime;
    static float _currentCooldownSeconds = CooldownSeconds;
    static bool? _publishedOnline;

    /// <summary>
    /// Set to true to simulate "no network" during debugging / offline-only UI.
    /// Takes precedence over reachability and the soft breaker.
    /// </summary>
    public static bool ForceOffline
    {
        get => _forceOffline;
        set
        {
            if (_forceOffline == value)
                return;
            _forceOffline = value;
            if (value)
                _consecutiveFailures = 0;
            Tick();
        }
    }

    static bool _forceOffline;

    /// <summary>
    /// Fired when <see cref="IsOnline"/> flips (hard disconnect, soft breaker, or recovery).
    /// Argument is the new online state. Requires periodic <see cref="Tick"/> for cooldown expiry.
    /// </summary>
    public static event Action<bool> IsOnlineChanged;

    /// <summary>
    /// True while the circuit breaker holds services in local-cache / queue mode
    /// even though the OS still reports a network interface.
    /// </summary>
    public static bool IsSoftOffline =>
        Time.realtimeSinceStartup < _cooldownUntilRealtime;

    /// <summary>
    /// True when services should attempt UGS network calls.
    /// False for hard disconnect, <see cref="ForceOffline"/>, or soft-breaker cooldown.
    /// </summary>
    public static bool IsOnline => ComputeIsOnline();

    /// <summary>
    /// Publishes <see cref="IsOnlineChanged"/> when reachability / soft-offline / ForceOffline
    /// changes. Call from a MonoBehaviour Update (or after ReportSuccess/ReportFailure).
    /// </summary>
    public static void Tick()
    {
        bool online = ComputeIsOnline();
        if (_publishedOnline == online)
            return;

        _publishedOnline = online;
        try
        {
            IsOnlineChanged?.Invoke(online);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// Call after a successful UGS round-trip that proves the link is usable.
    /// Clears soft-offline and resets escalation.
    /// </summary>
    public static void ReportSuccess()
    {
        _consecutiveFailures = 0;
        _firstFailureRealtime = 0f;
        _currentCooldownSeconds = CooldownSeconds;
        if (_cooldownUntilRealtime > 0f)
        {
            _cooldownUntilRealtime = 0f;
            Debug.Log("[SDK][Network] Soft-offline cleared after successful request.");
        }

        Tick();
    }

    /// <summary>
    /// Call after a recoverable / indeterminate transport failure.
    /// After <see cref="FailureThreshold"/> failures inside <see cref="FailureWindowSeconds"/>,
    /// enters soft-offline cooldown (escalating 20→40→80s).
    /// </summary>
    public static void ReportFailure()
    {
        if (_forceOffline)
        {
            _consecutiveFailures = 0;
            Tick();
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (_consecutiveFailures == 0
            || now - _firstFailureRealtime > FailureWindowSeconds)
        {
            _consecutiveFailures = 1;
            _firstFailureRealtime = now;
        }
        else
        {
            _consecutiveFailures++;
        }

        if (_consecutiveFailures < FailureThreshold)
        {
            Debug.LogWarning(
                $"[SDK][Network] Transport failure {_consecutiveFailures}/{FailureThreshold} " +
                $"(window {FailureWindowSeconds:0}s).");
            Tick();
            return;
        }

        _consecutiveFailures = 0;
        _firstFailureRealtime = 0f;
        _cooldownUntilRealtime = now + _currentCooldownSeconds;
        Debug.LogWarning(
            $"[SDK][Network] Soft-offline for {_currentCooldownSeconds:0}s after repeated UGS failures " +
            "(timeouts / DPI / poor link). Services will use local cache.");
        _currentCooldownSeconds = Mathf.Min(MaxCooldownSeconds, _currentCooldownSeconds * 2f);
        Tick();
    }

    /// <summary>Clears soft-offline after app resume so a healthy link is re-probed promptly.</summary>
    public static void NotifyApplicationResumed()
    {
        if (_cooldownUntilRealtime > 0f)
        {
            _cooldownUntilRealtime = 0f;
            Debug.Log("[SDK][Network] Soft-offline cleared on application resume.");
        }

        Tick();
    }

    static bool ComputeIsOnline() =>
        !_forceOffline
        && !IsSoftOffline
        && Application.internetReachability != NetworkReachability.NotReachable;
}
