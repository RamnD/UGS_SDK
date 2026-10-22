using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RamnD.SdkSmoke
{
    public sealed class SmokeTransport : IRamndAnalyticsTransport
    {
        public List<string> Bodies { get; } = new List<string>();
        public int FailuresRemaining { get; set; }

        public async Task<RamndIngestResult> PostEventsAsync(
            string jsonBody, CancellationToken cancellationToken = default)
        {
            // Resume through Unity's real synchronization context instead of completing inline.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Bodies.Add(jsonBody);
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                return new RamndIngestResult(503, "{\"error\":\"smoke-test transient failure\"}");
            }

            return new RamndIngestResult(200, "{\"accepted\":1,\"rejected\":[]}");
        }
    }
}
