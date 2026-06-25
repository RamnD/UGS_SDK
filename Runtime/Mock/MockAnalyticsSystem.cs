using UnityEngine;

/// <summary>
/// Mock-реализация <see cref="IAnalyticsSystem"/>.
/// Выводит события в консоль — никуда не отправляет.
/// <para>
/// Используйте для разработки UI/логики до подключения реального Analytics SDK.
/// </para>
/// </summary>
public sealed class MockAnalyticsSystem : IAnalyticsSystem
{
    /// <inheritdoc/>
    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        Debug.Log($"[Mock Analytics] Event: {eventPayload.EventName}");
    }

    /// <inheritdoc/>
    public void Flush()
    {
        Debug.Log("[Mock Analytics] Flush (mock, nothing to send).");
    }
}
