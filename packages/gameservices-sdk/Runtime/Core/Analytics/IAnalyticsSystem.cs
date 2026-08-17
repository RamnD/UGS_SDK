/// <summary>
/// Analytics system. Abstracted from the concrete SDK (UGS, Firebase, GameAnalytics…).
/// All events are typed via <see cref="IAnalyticsEvent"/> — no string keys outside event structs.
/// </summary>
public interface IAnalyticsSystem
{
    /// <summary>
    /// Records an analytics event.
    /// Fields on T with <see cref="AnalyticsKeyAttribute"/> are serialized as event parameters.
    /// </summary>
    void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent;

    /// <summary>
    /// Forces delivery of queued events to the server.
    /// Call on pause and app exit. No need to call after every <see cref="LogEvent{T}"/>.
    /// </summary>
    void Flush();
}
