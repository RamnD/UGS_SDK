using UnityEngine;

/// <summary>
/// Mock <see cref="IAnalyticsSystem"/> implementation.
/// Logs events to the console — does not send anywhere.
/// <para>
/// Use for UI/logic development before wiring a real Analytics SDK.
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
