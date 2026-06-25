using UnityEngine;

/// <summary>
/// Single entry point to all services after initialization (UGSServicesBuilder or test MockGameServices).
/// Call Set from bootstrap once before use.
/// </summary>
public static class GameServicesLocator
{
    private static IGameServices _services;

    /// <summary>Active façade. Null if <see cref="Set"/> has not been called yet.</summary>
    public static IGameServices Services => _services;

    /// <inheritdoc cref="Services"/>
    public static IGameServices Instance => _services;

    public static bool IsInitialized => _services != null;

    public static bool TryGet(out IGameServices services)
    {
        services = _services;
        return services != null;
    }

    /// <summary>
    /// Sets the façade. Call from the bootstrap builder after a successful async service build.
    /// </summary>
    public static void Set(IGameServices services)
    {
        if (services != null && _services != null)
            Debug.LogWarning("[GameServices] GameServicesLocator.Set: facade already set — overwriting.");
        _services = services;
    }

    /// <summary>Reset on domain reload (tests, Editor). Usually not needed at runtime.</summary>
    public static void Clear() => _services = null;
}
