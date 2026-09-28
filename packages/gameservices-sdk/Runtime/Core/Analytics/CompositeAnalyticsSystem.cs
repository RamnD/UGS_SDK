using System;

/// <summary>
/// Fans out analytics to one or two <see cref="IAnalyticsSystem"/> backends.
/// Failures in one backend do not prevent the other from receiving the event.
/// </summary>
public sealed class CompositeAnalyticsSystem : IAnalyticsSystem
{
    readonly IAnalyticsSystem _ugs;
    readonly IAnalyticsSystem _ramnd;
    readonly AnalyticsBackendMode _mode;

    public CompositeAnalyticsSystem(
        AnalyticsBackendMode mode,
        IAnalyticsSystem ugs = null,
        IAnalyticsSystem ramnd = null)
    {
        _mode = mode;
        _ugs = ugs;
        _ramnd = ramnd;

        switch (mode)
        {
            case AnalyticsBackendMode.Ugs:
                if (_ugs == null)
                    throw new ArgumentNullException(nameof(ugs));
                break;
            case AnalyticsBackendMode.Ramnd:
                if (_ramnd == null)
                    throw new ArgumentNullException(nameof(ramnd));
                break;
            case AnalyticsBackendMode.Both:
                if (_ugs == null)
                    throw new ArgumentNullException(nameof(ugs));
                if (_ramnd == null)
                    throw new ArgumentNullException(nameof(ramnd));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    public AnalyticsBackendMode Mode => _mode;

    /// <inheritdoc/>
    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
        if (UsesUgs)
            SafeLog(_ugs, eventPayload, "UGS");
        if (UsesRamnd)
            SafeLog(_ramnd, eventPayload, "Ramnd");
    }

    /// <inheritdoc/>
    public void Flush()
    {
        if (UsesUgs)
            SafeFlush(_ugs, "UGS");
        if (UsesRamnd)
            SafeFlush(_ramnd, "Ramnd");
    }

    bool UsesUgs =>
        _mode == AnalyticsBackendMode.Ugs || _mode == AnalyticsBackendMode.Both;

    bool UsesRamnd =>
        _mode == AnalyticsBackendMode.Ramnd || _mode == AnalyticsBackendMode.Both;

    static void SafeLog<T>(IAnalyticsSystem backend, T eventPayload, string label)
        where T : struct, IAnalyticsEvent
    {
        try
        {
            backend.LogEvent(eventPayload);
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"{label} LogEvent '{eventPayload.EventName}' failed: {ex.Message}");
        }
    }

    static void SafeFlush(IAnalyticsSystem backend, string label)
    {
        try
        {
            backend.Flush();
        }
        catch (Exception ex)
        {
            AppLog.Error("Analytics", $"{label} Flush failed: {ex.Message}");
        }
    }
}
