using UnityEngine;

/// <summary>
/// Единая точка входа ко всем сервисам после инициализации (UGSServicesBuilder или тестовый MockGameServices).
/// Вызывайте Set из bootstrap один раз перед использованием.
/// </summary>
public static class GameServicesLocator
{
    private static IGameServices _services;

    /// <summary>Активный фасад. Null если <see cref="Set"/> ещё не вызывался.</summary>
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
    /// Устанавливает фасад. Вызывайте из bootstrap-билдера после успешной асинхронной сборки сервисов.
    /// </summary>
    public static void Set(IGameServices services)
    {
        if (services != null && _services != null)
            Debug.LogWarning("[GameServices] GameServicesLocator.Set: facade already set — overwriting.");
        _services = services;
    }

    /// <summary>Сброс при перезапуске домена (тесты, Editor). Обычно не нужен в рантайме.</summary>
    public static void Clear() => _services = null;
}
