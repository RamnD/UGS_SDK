using System;
using System.Collections;
using UnityEngine;

namespace RamnD.SdkSmoke
{
    public sealed class SdkSmokeBootstrap : MonoBehaviour
    {
        RamndAnalyticSystem _analytics;
        public bool Completed { get; private set; }
        public SmokeTransport Transport { get; private set; }

        IEnumerator Start()
        {
            SmokeEnvironment.ResetQueue();
            Transport = new SmokeTransport();
            _analytics = SmokeEnvironment.CreateSystem(Transport, TimeSpan.Zero);
            _analytics.LogEvent(new SmokeEvent("scene-smoke"));
            var flush = _analytics.FlushAsync();
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!flush.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!flush.IsCompleted)
                throw new TimeoutException("Scene analytics flush did not complete within 10 seconds.");
            flush.GetAwaiter().GetResult();
            if (Transport.Bodies.Count == 0)
                throw new InvalidOperationException("Scene bootstrap did not send an analytics event.");
            Completed = true;
            Debug.Log("SDK smoke scene completed using the local transport.");
        }

        void OnDestroy()
        {
            _analytics?.Dispose();
            _analytics = null;
        }
    }
}
