using System;
using System.IO;
using UnityEngine;

namespace RamnD.SdkSmoke
{
    public static class SmokeEnvironment
    {
        public const string ProductName = "UGS SDK Smoke Tests";

        public static RamndAnalyticsConfig CreateConfig(TimeSpan interval) =>
            new RamndAnalyticsConfig("https://sdk-smoke.invalid", "smoke-only", 500, interval);

        public static RamndAnalyticSystem CreateSystem(SmokeTransport transport, TimeSpan interval) =>
            new RamndAnalyticSystem(CreateConfig(interval), transport, "smoke-install", "EDITOR", "smoke-player");

        public static void ResetQueue()
        {
            // The public SDK constructor uses persistentDataPath. Never clean a consuming game's data.
            if (!string.Equals(Application.productName, ProductName, StringComparison.Ordinal))
                throw new InvalidOperationException("Run the smoke harness only from TestProject.");

            Directory.CreateDirectory(Application.persistentDataPath);
            foreach (string name in new[] { "ramnd-analytics-queue.json", "ramnd-analytics-queue.journal.jsonl" })
            {
                string path = Path.Combine(Application.persistentDataPath, name);
                if (File.Exists(path))
                    File.Delete(path);
                if (File.Exists(path + ".tmp"))
                    File.Delete(path + ".tmp");
            }
        }
    }

    public readonly struct SmokeEvent : IAnalyticsEvent
    {
        public string EventName => "app_session_start";
        [AnalyticsKey("build_version")] public readonly string BuildVersion;

        public SmokeEvent(string buildVersion) => BuildVersion = buildVersion;
    }
}
