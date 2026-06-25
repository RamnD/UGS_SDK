/// <summary>
/// Поддерживаемые стратегии аутентификации.
/// Намеренно не совпадает с <see cref="UnityEngine.RuntimePlatform"/> —
/// здесь только то, что реально реализовано в auth-слое.
/// </summary>
public enum AuthPlatform
{
    /// <summary>Анонимный вход — не привязан к платформе. Используется по умолчанию и в редакторе.</summary>
    Anonymous,

    /// <summary>Google Play Games — только Android. Требует настройки GPGS SDK.</summary>
    GooglePlayGames,

    /// <summary>Apple Sign-In — только iOS. Требует нативного плагина для получения identityToken.</summary>
    Apple
}
