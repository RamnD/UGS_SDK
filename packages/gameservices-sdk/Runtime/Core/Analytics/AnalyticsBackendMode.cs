/// <summary>
/// Which analytics backends receive <see cref="IAnalyticsSystem.LogEvent{T}"/>.
/// Default for existing games remains <see cref="Ugs"/>.
/// </summary>
public enum AnalyticsBackendMode
{
    /// <summary>Unity Gaming Services Analytics only (current default).</summary>
    Ugs = 0,

    /// <summary>Self-hosted RamnD game-services gateway only.</summary>
    Ramnd = 1,

    /// <summary>Fan-out to both UGS and RamnD; failures are independent.</summary>
    Both = 2,
}
