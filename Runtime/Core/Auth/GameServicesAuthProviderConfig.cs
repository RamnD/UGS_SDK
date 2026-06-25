/// <summary>
/// Необязательные параметры платформенных провайдеров для связки с UGS Authentication.
/// Ключи в проектах часто задаются через ScriptableObject, Remote Config или CI — соберите их в объект и передайте в билдер через WithAuthProviderCredentials.
/// <para>
/// TODO: использовать идентификаторы в нативных потоках GPGS / Sign in with Apple перед вызовом UGS SDK.
/// </para>
/// </summary>
public sealed class GameServicesAuthProviderConfig
{
    public static GameServicesAuthProviderConfig Empty => new GameServicesAuthProviderConfig();

    /// <summary>
    /// TODO(Google Play Games → UGS): Web Client Id / OAuth-клиент, если требуется вашей связкой GPGS + UGS
    /// (зависит от настройки Play Console и плагина). Пустое — платформенные методы должны трактовать как «ключ не передан».
    /// </summary>
    public string GooglePlayGamesOAuthWebClientId { get; set; }

    /// <summary>
    /// TODO(Apple Sign-In → UGS): Services ID или иной идентификатор приложения для Apple + UGS. Пустое — методы Sign-In/link на iOS недоступны (см. лог Auth).
    /// </summary>
    public string AppleServicesId { get; set; }
}
