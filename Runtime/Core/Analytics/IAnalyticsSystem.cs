/// <summary>
/// Система аналитики. Абстрагирована от конкретного SDK (UGS, Firebase, GameAnalytics…).
/// Все события типизированы через <see cref="IAnalyticsEvent"/> — нет строковых ключей вне событий.
/// </summary>
public interface IAnalyticsSystem
{
    /// <summary>
    /// Записывает событие аналитики.
    /// Поля T с атрибутом <see cref="AnalyticsKeyAttribute"/> сериализуются как параметры события.
    /// </summary>
    void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent;

    /// <summary>
    /// Принудительно отправляет накопленные события на сервер.
    /// Вызывайте при паузе и выходе из приложения. Не нужно вызывать после каждого <see cref="LogEvent{T}"/>.
    /// </summary>
    void Flush();
}
