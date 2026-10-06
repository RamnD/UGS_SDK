using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        public string Body = "{\"accepted\":1}";

        public Task<RamndIngestResult> PostEventsAsync(
            string jsonBody,
            CancellationToken cancellationToken = default)
        {
            Bodies.Add(jsonBody);
            return Task.FromResult(new RamndIngestResult(StatusCode, Body));
        }
    }

    sealed class MemoryQueueStore : IRamndAnalyticsQueueStore
    {
        string _json = string.Empty;

        public int Count { get; private set; }

        public List<Dictionary<string, object>> Load()
        {
            if (string.IsNullOrEmpty(_json))
                return new List<Dictionary<string, object>>();
            return JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(_json)
                ?? new List<Dictionary<string, object>>();
        }

        public void Save(IReadOnlyList<Dictionary<string, object>> events)
        {
            Count = events?.Count ?? 0;
            _json = JsonConvert.SerializeObject(events ?? Array.Empty<Dictionary<string, object>>());
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
        var store = new MemoryQueueStore();
        var system = CreateSystem(transport, store);

        system.LogEvent(new SampleEvent("1.2.3", true));
        await system.FlushAsync();

        Assert.That(transport.Bodies.Count, Is.EqualTo(1));
        var root = JObject.Parse(transport.Bodies[0]);
        var events = (JArray)root["events"];
        Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(events[0]!["event_name"]!.Value<string>(), Is.EqualTo("app_session_start"));
        Assert.That(events[0]!["build_version"]!.Value<string>(), Is.EqualTo("1.2.3"));
        Assert.That(events[0]!["user_id"]!.Value<string>(), Is.EqualTo("install-test"));
        Assert.That(store.Count, Is.EqualTo(0));
        system.Dispose();
    }

    [Test]
    public async Task Flush_OnTransportFailure_DoesNotSpinRetryLoop()
    {
        var transport = new FakeTransport { StatusCode = 503 };
        var store = new MemoryQueueStore();
        var system = CreateSystem(transport, store);

        system.LogEvent(new SampleEvent("1.2.3", true));
        await system.FlushAsync();
        await system.FlushAsync();

        Assert.That(transport.Bodies.Count, Is.EqualTo(2), "One attempt per Flush; no tight retry loop");
        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(EventIdOf(transport.Bodies[0]), Is.EqualTo(EventIdOf(transport.Bodies[1])));
        system.Dispose();
    }

    [Test]
    public async Task Dispose_KeepsUnsentEventsForNextSession()
    {
        var store = new MemoryQueueStore();
        var firstTransport = new FakeTransport { StatusCode = 503 };
        var first = CreateSystem(firstTransport, store);
        first.LogEvent(new SampleEvent("1.2.3", true));
        await first.FlushAsync();
        string eventId = EventIdOf(firstTransport.Bodies[0]);
        first.Dispose();

        Assert.That(store.Count, Is.EqualTo(1));

        var secondTransport = new FakeTransport { StatusCode = 200 };
        var second = CreateSystem(secondTransport, store);
        await second.FlushAsync();

        Assert.That(secondTransport.Bodies.Count, Is.EqualTo(1));
        Assert.That(EventIdOf(secondTransport.Bodies[0]), Is.EqualTo(eventId));
        Assert.That(store.Count, Is.EqualTo(0));
        second.Dispose();
    }

    [Test]
    public async Task Http400WithoutRejectedList_KeepsTheBatch()
    {
        var transport = new FakeTransport
        {
            StatusCode = 400,
            Body = "{\"error\":\"unknown_app\"}",
        };
        var store = new MemoryQueueStore();
        var system = CreateSystem(transport, store);

        system.LogEvent(new SampleEvent("1.2.3", true));
        await system.FlushAsync();

        Assert.That(store.Count, Is.EqualTo(1));
        system.Dispose();
    }

    [Test]
    public async Task Http400RejectedEvent_IsDroppedAndDoesNotBlockLaterEvents()
    {
        var transport = new RejectingTransport();
        var store = new MemoryQueueStore();
        var system = CreateSystem(transport, store);

        system.LogEvent(new SampleEvent("bad", true));
        await system.FlushAsync();
        system.LogEvent(new SampleEvent("good", false));
        await system.FlushAsync();

        Assert.That(transport.Bodies.Count, Is.EqualTo(2));
        Assert.That(EventIdOf(transport.Bodies[0]), Is.Not.EqualTo(EventIdOf(transport.Bodies[1])));
        var secondBatch = (JArray)JObject.Parse(transport.Bodies[1])["events"];
        Assert.That(secondBatch.Count, Is.EqualTo(1));
        Assert.That(secondBatch[0]!["build_version"]!.Value<string>(), Is.EqualTo("good"));
        Assert.That(store.Count, Is.EqualTo(0));
        system.Dispose();
    }

    [Test]
    public async Task QueuePastCap_DropsOldestEvent()
    {
        var transport = new FakeTransport { StatusCode = 503 };
        var store = new MemoryQueueStore();
        var system = CreateSystem(transport, store);

        system.LogEvent(new SampleEvent("oldest", true));
        for (int i = 0; i < RamndAnalyticSystem.DiskQueueCap - 2; i++)
            system.LogEvent(new SampleEvent("mid-" + i, false));

        string oldestId = EventIdFromStore(store, 0);
        system.LogEvent(new SampleEvent("fills-batch", false));
        await system.FlushAsync();
        system.LogEvent(new SampleEvent("overflow", false));

        Assert.That(store.Count, Is.EqualTo(RamndAnalyticSystem.DiskQueueCap));
        Assert.That(StoreContains(store, oldestId), Is.False);
        system.Dispose();
    }

    [Test]
    public void FileQueue_RoundTripsEnvelopesForTheSameGateway()
    {
        string dir = CreateTempDir();
        try
        {
            var store = new RamndAnalyticsFileQueue(dir, "http://localhost:3000");
            var row = new Dictionary<string, object>
            {
                ["event_id"] = "22222222-2222-4222-8222-222222222222",
                ["event_name"] = "app_session_start",
                ["event_timestamp"] = "2026-09-28T12:00:00.000Z",
            };
            store.Save(new[] { row });

            var loaded = store.Load();
            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(loaded[0]["event_id"].ToString(), Is.EqualTo(row["event_id"]));
            Assert.That(loaded[0]["event_timestamp"].ToString(), Does.Contain("2026-09-28"));

            var otherGateway = new RamndAnalyticsFileQueue(dir, "http://prod.example");
            Assert.That(otherGateway.Load().Count, Is.EqualTo(0));
            Assert.That(store.Load().Count, Is.EqualTo(1), "A mismatched load must not delete the other gateway's file.");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void FileQueue_UnreadableFile_LoadsEmpty()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "ramnd-analytics-queue.json"), "{not json");
            var store = new RamndAnalyticsFileQueue(dir, "http://localhost:3000");
            Assert.That(store.Load().Count, Is.EqualTo(0));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void IngestResult_207_IsSuccess()
    {
        Assert.That(new RamndIngestResult(207, "{}").IsSuccess, Is.True);
        Assert.That(new RamndIngestResult(200, "{}").IsSuccess, Is.True);
        Assert.That(new RamndIngestResult(400, "{}").IsSuccess, Is.False);
    }

    static RamndAnalyticSystem CreateSystem(IRamndAnalyticsTransport transport, IRamndAnalyticsQueueStore store) =>
        new RamndAnalyticSystem(
            new RamndAnalyticsConfig("http://localhost:3000", "dev-maze-key"),
            transport,
            "install-test",
            "EDITOR",
            "p1",
            store);

    static string EventIdOf(string body) =>
        JObject.Parse(body)["events"]![0]!["event_id"]!.Value<string>();

    static string EventIdFromStore(MemoryQueueStore store, int index)
    {
        var loaded = store.Load();
        Assert.That(loaded.Count, Is.GreaterThan(index));
        return loaded[index]["event_id"].ToString();
    }

    static bool StoreContains(MemoryQueueStore store, string eventId)
    {
        foreach (var row in store.Load())
        {
            if (string.Equals(row["event_id"]?.ToString(), eventId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static string CreateTempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "ramnd-analytics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    sealed class RejectingTransport : IRamndAnalyticsTransport
    {
        public readonly List<string> Bodies = new List<string>();
        int _calls;

        public Task<RamndIngestResult> PostEventsAsync(
            string jsonBody,
            CancellationToken cancellationToken = default)
        {
            Bodies.Add(jsonBody);
            _calls++;
            if (_calls == 1)
            {
                string id = EventIdOf(jsonBody);
                string body = JsonConvert.SerializeObject(new
                {
                    rejected = new[] { new { eventId = id, index = 0, reason = "unknown_event" } },
                });
                return Task.FromResult(new RamndIngestResult(400, body));
            }

            return Task.FromResult(new RamndIngestResult(200, "{\"accepted\":1}"));
        }
    }
}
