using System;

/// <summary>
/// Connection settings for RamnD analytics HTTP ingest (game-services gateway).
/// </summary>
public sealed class RamndAnalyticsConfig
{
    const int DefaultMaxBatchSize = 500;

    /// <summary>Public gateway base URL, e.g. <c>http://localhost:3000</c> (no trailing slash required).</summary>
    public string BaseUrl { get; }

    /// <summary>Game API key sent as <c>X-Api-Key</c>.</summary>
    public string ApiKey { get; }

    /// <summary>Max events per ingest POST (gateway default / hard limit is 500).</summary>
    public int MaxBatchSize { get; }

    public RamndAnalyticsConfig(string baseUrl, string apiKey, int maxBatchSize = DefaultMaxBatchSize)
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

        BaseUrl = baseUrl.TrimEnd('/');
        ApiKey = apiKey.Trim();
        MaxBatchSize = maxBatchSize;
    }
}
