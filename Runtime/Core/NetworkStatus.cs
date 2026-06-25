using UnityEngine;

/// <summary>
/// Утилита для проверки наличия сетевого подключения.
/// Используется сервисами SDK для переключения между онлайн и офлайн режимами.
/// </summary>
public static class NetworkStatus
{
    /// <summary>
    /// Можно выставить true для симуляции «без сети» во время отладки / UI «только офлайн».
    /// Имеет приоритет над <see cref="Application.internetReachability"/>.
    /// </summary>
    public static bool ForceOffline { get; set; }

    /// <summary>
    /// True если устройство имеет активное подключение (по умолчанию через
    /// <see cref="Application.internetReachability"/>) и не включено <see cref="ForceOffline"/>.
    /// Эвристика: не выполняет реальный запрос к серверу.
    /// </summary>
    public static bool IsOnline =>
        !ForceOffline && Application.internetReachability != NetworkReachability.NotReachable;
}
