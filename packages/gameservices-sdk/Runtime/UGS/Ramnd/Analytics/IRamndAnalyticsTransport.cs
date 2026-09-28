using System.Threading;
using System.Threading.Tasks;

/// <summary>Result of posting an ingest batch to the game-services gateway.</summary>
public readonly struct RamndIngestResult
{
    public int StatusCode { get; }
    public string Body { get; }

    public RamndIngestResult(int statusCode, string body)
    {
        StatusCode = statusCode;
        Body = body ?? string.Empty;
    }

    /// <summary>Gateway treats 200 and 207 as successful acceptance of the batch.</summary>
    public bool IsSuccess => StatusCode == 200 || StatusCode == 207;
}

/// <summary>HTTP transport used by <see cref="RamndAnalyticSystem"/> (injectable for tests).</summary>
public interface IRamndAnalyticsTransport
{
    Task<RamndIngestResult> PostEventsAsync(string jsonBody, CancellationToken cancellationToken = default);
}
