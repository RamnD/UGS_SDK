using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class RamndAnalyticsTests
{
    readonly struct SampleEvent : IAnalyticsEvent
    {
        public string EventName => "app_session_start";

        [AnalyticsKey("build_version")]
        public readonly string BuildVersion;

        [AnalyticsKey("is_first_launch")]
        public readonly bool IsFirstLaunch;

        public SampleEvent(string buildVersion, bool isFirstLaunch)
        {
            BuildVersion = buildVersion;
            IsFirstLaunch = isFirstLaunch;
        }
    }

    sealed class FakeTransport : IRamndAnalyticsTransport
    {
        public readonly List<string> Bodies = new List<string>();
        public int StatusCode = 200;

        public Task<RamndIngestResult> PostEventsAsync(
            string jsonBody,
            CancellationToken cancellationToken = default)
        {
            Bodies.Add(jsonBody);
            return Task.FromResult(new RamndIngestResult(StatusCode, "{\"accepted\":1}"));
        }
    }

    sealed class CountingAnalytics : IAnalyticsSystem
    {
        public int LogCount;
        public int FlushCount;

        public void LogEvent<T>(T eventPayload) where T : struct, IAnalyticsEvent => LogCount++;
        public void Flush() => FlushCount++;
    }

    [Test]
    public void Envelope_ContainsRequiredFieldsAndAnalyticsKeyParams()
    {
        var row = RamndAnalyticsEnvelopeBuilder.BuildEventObject(
            new SampleEvent("1.0.0-dev", true),
            userId: "install-1",
            platform: "ANDROID",
            playerId: "player-42",
            utcNow: new System.DateTime(2026, 9, 28, 12, 0, 0, System.DateTimeKind.Utc));

        Assert.That(row["event_name"], Is.EqualTo("app_session_start"));
        Assert.That(row["user_id"], Is.EqualTo("install-1"));
        Assert.That(row["platform"], Is.EqualTo("ANDROID"));
        Assert.That(row["player_id"], Is.EqualTo("player-42"));
        Assert.That(row["build_version"], Is.EqualTo("1.0.0-dev"));
        Assert.That(row["is_first_launch"], Is.EqualTo(true));
        Assert.That(row["event_id"], Is.Not.Null.And.Not.Empty);
        Assert.That(row["event_timestamp"].ToString(), Does.Contain("2026-09-28"));
    }

    [Test]
    public void Composite_Both_FansOutToUgsAndRamnd()
    {
        var ugs = new CountingAnalytics();
        var ramnd = new CountingAnalytics();
        var composite = new CompositeAnalyticsSystem(AnalyticsBackendMode.Both, ugs, ramnd);

        composite.LogEvent(new SampleEvent("1.0.0", false));
        composite.Flush();

        Assert.That(ugs.LogCount, Is.EqualTo(1));
        Assert.That(ramnd.LogCount, Is.EqualTo(1));
        Assert.That(ugs.FlushCount, Is.EqualTo(1));
        Assert.That(ramnd.FlushCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Flush_PostsBatch_AndTreats207AsSuccess()
    {
        var transport = new FakeTransport { StatusCode = 207 };
        var config = new RamndAnalyticsConfig("http://localhost:3000", "dev-maze-key");
        var system = new RamndAnalyticSystem(
            config,
            transport,
            userId: "install-test",
            platform: "EDITOR",
            playerId: "p1");

        system.LogEvent(new SampleEvent("1.2.3", true));
        await system.FlushAsync();

        Assert.That(transport.Bodies.Count, Is.EqualTo(1));
        var root = JObject.Parse(transport.Bodies[0]);
        var events = (JArray)root["events"];
        Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(events[0]!["event_name"]!.Value<string>(), Is.EqualTo("app_session_start"));
        Assert.That(events[0]!["build_version"]!.Value<string>(), Is.EqualTo("1.2.3"));
        Assert.That(events[0]!["user_id"]!.Value<string>(), Is.EqualTo("install-test"));
    }

    [Test]
    public void IngestResult_207_IsSuccess()
    {
        Assert.That(new RamndIngestResult(207, "{}").IsSuccess, Is.True);
        Assert.That(new RamndIngestResult(200, "{}").IsSuccess, Is.True);
        Assert.That(new RamndIngestResult(400, "{}").IsSuccess, Is.False);
    }
}
