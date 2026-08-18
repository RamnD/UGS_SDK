/// <summary>
/// Single entry point to all services after initialization
/// (<see cref="UGSServicesBuilder"/> or <see cref="MockGameServices"/>).
/// Call <see cref="Set"/> from bootstrap once before use.
/// </summary>
public static class GameServicesLocator
{
    private static IGameServices _services;

    /// <summary>Active façade. Null if <see cref="Set"/> has not been called yet.</summary>
    public static IGameServices Services => _services;

    /// <summary>Alias for <see cref="Services"/> (legacy name).</summary>
    public static IGameServices Instance => _services;

    /// <summary>True when <see cref="Set"/> has registered a non-null façade.</summary>
    public static bool IsInitialized => _services != null;

    /// <summary>
    /// Tries to read the active façade without throwing.
    /// </summary>
    /// <param name="services">The registered façade, or null when not initialized.</param>
    /// <returns>True when a façade is registered.</returns>
    public static bool TryGet(out IGameServices services)
    {
        services = _services;
        return services != null;
    }

    /// <summary>
    /// Sets the façade. Call from the bootstrap builder after a successful async service build.
    /// Overwriting an already-set façade logs a warning.
    /// </summary>
    /// <param name="services">Façade instance (may be null to clear via overwrite — prefer <see cref="Clear"/>).</param>
    public static void Set(IGameServices services)
    {
        if (services != null && _services != null)
            AppLog.Warn("GameServices", "GameServicesLocator.Set: facade already set — overwriting.");
        _services = services;
    }

    /// <summary>Reset on domain reload (tests, Editor). Usually not needed at runtime.</summary>
    public static void Clear() => _services = null;
}
