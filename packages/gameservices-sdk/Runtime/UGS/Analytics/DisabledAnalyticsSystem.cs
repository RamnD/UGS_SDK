/// <summary>
/// No-op <see cref="IAnalyticsSystem"/> used when UGS collection must not start
/// (Editor Play + <c>UGS_ENV_PRODUCTION</c>). Keeps the locator non-null.
/// </summary>
sealed class DisabledAnalyticsSystem : IAnalyticsSystem
{
    public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent
    {
    }

    public void Flush()
    {
    }
}
