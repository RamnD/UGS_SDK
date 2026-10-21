using System;

/// <summary>
/// Connection settings for RamnD analytics HTTP ingest (game-services gateway).
/// </summary>
public sealed class RamndAnalyticsConfig
{
    const int DefaultMaxBatchSize = 500;

    /// <summary>How often a non-empty queue is sent while the device is online.</summary>
    public static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(15);

    /// <summary>Public gateway base URL, e.g. <c>http://localhost:3000</c> (no trailing slash required).</summary>
    public string BaseUrl { get; }

    /// <summary>Game API key sent as <c>X-Api-Key</c>.</summary>
    public string ApiKey { get; }

    /// <summary>Max events per ingest POST (gateway default / hard limit is 500).</summary>
    public int MaxBatchSize { get; }

    /// <summary>
    /// Interval between automatic flushes while online.
    /// <see cref="TimeSpan.Zero"/> disables the timer; <c>Flush()</c> still sends immediately.
    /// </summary>
    public TimeSpan FlushInterval { get; }

    /// <summary>
    /// Original constructor. <paramref name="maxBatchSize"/> defaults to 500 and
    /// <see cref="FlushInterval"/> stays <see cref="DefaultFlushInterval"/>.
    /// </summary>
    /// <remarks>
    /// This three-argument signature is the binary contract. Flush interval is a separate
    /// overload so existing <c>(url, key)</c> and <c>(url, key, batch)</c> calls keep binding here.
    /// </remarks>
    public RamndAnalyticsConfig(
        string baseUrl,
        string apiKey,
        int maxBatchSize = DefaultMaxBatchSize)
        : this(baseUrl, apiKey, maxBatchSize, flushInterval: null)
    {
    }

    /// <summary>
    /// Uses <see cref="DefaultMaxBatchSize"/> and the given flush interval.
    /// Null uses <see cref="DefaultFlushInterval"/>. <see cref="TimeSpan.Zero"/> disables the timer.
    /// </summary>
    /// <remarks>
    /// <paramref name="flushInterval"/> is required so this overload does not compete with
    /// the original two- and three-argument calls. Named <c>flushInterval:</c> calls bind here
    /// without supplying <c>maxBatchSize</c>.
    /// </remarks>
    public RamndAnalyticsConfig(
        string baseUrl,
        string apiKey,
        TimeSpan? flushInterval)
        : this(baseUrl, apiKey, DefaultMaxBatchSize, flushInterval)
    {
    }

    /// <summary>
    /// Sets batch size and flush interval together.
    /// <paramref name="maxBatchSize"/> is required so this overload does not hide the original constructor.
    /// Null <paramref name="flushInterval"/> uses <see cref="DefaultFlushInterval"/>.
    /// </summary>
    public RamndAnalyticsConfig(
        string baseUrl,
        string apiKey,
        int maxBatchSize,
        TimeSpan? flushInterval)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("BaseUrl is required.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("ApiKey is required.", nameof(apiKey));
        if (maxBatchSize <= 0 || maxBatchSize > DefaultMaxBatchSize)
            throw new ArgumentOutOfRangeException(
                nameof(maxBatchSize),
                maxBatchSize,
                $"MaxBatchSize must be 1..{DefaultMaxBatchSize}.");

        TimeSpan interval = flushInterval ?? DefaultFlushInterval;
        if (interval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(flushInterval),
                interval,
                "FlushInterval must be zero or positive.");

        BaseUrl = baseUrl.TrimEnd('/');
        ApiKey = apiKey.Trim();
        MaxBatchSize = maxBatchSize;
        FlushInterval = interval;
    }
}
