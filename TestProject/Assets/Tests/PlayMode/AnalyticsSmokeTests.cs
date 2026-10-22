using System;
using System.Collections;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RamnD.SdkSmoke;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class AnalyticsSmokeTests
{
    RamndAnalyticSystem _analytics;
    SmokeTransport _transport;
    bool _wasOffline;
    Scene _scene;

    [SetUp]
    public void SetUp()
    {
        Assert.That(LogAssert.ignoreFailingMessages, Is.False, "Unexpected Unity errors must fail tests.");
        _wasOffline = NetworkStatus.ForceOffline;
        NetworkStatus.ForceOffline = true;
        SmokeEnvironment.ResetQueue();
        _transport = new SmokeTransport();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _analytics?.Dispose();
        _analytics = null;
        if (_scene.IsValid() && _scene.isLoaded)
        {
            var unload = SceneManager.UnloadSceneAsync(_scene);
            yield return Until(() => unload.isDone);
        }
        yield return null;
        SmokeEnvironment.ResetQueue();
        NetworkStatus.ForceOffline = _wasOffline;
    }

    [UnityTest]
    public IEnumerator SmokeScene_BootstrapsAndSendsEvent()
    {
        var load = SceneManager.LoadSceneAsync("Assets/Scenes/SdkSmoke.unity", LoadSceneMode.Additive);
        yield return Until(() => load.isDone);
        _scene = SceneManager.GetSceneByPath("Assets/Scenes/SdkSmoke.unity");
        SdkSmokeBootstrap bootstrap = null;
        foreach (var root in _scene.GetRootGameObjects())
        {
            bootstrap = root.GetComponentInChildren<SdkSmokeBootstrap>();
            if (bootstrap != null)
                break;
        }
        Assert.That(bootstrap, Is.Not.Null);
        yield return Until(() => bootstrap.Completed);
        Assert.That(Events(bootstrap.Transport.Bodies[0])[0]["build_version"].Value<string>(), Is.EqualTo("scene-smoke"));
    }

    [UnityTest]
    public IEnumerator PeriodicFlush_SendsWithoutExplicitFlush()
    {
        NetworkStatus.ForceOffline = false;
        Assert.That(NetworkStatus.IsOnline, Is.True, "Automatic delivery needs a reachable network interface, but never calls a backend.");
        _analytics = SmokeEnvironment.CreateSystem(_transport, TimeSpan.FromMilliseconds(100));
        _analytics.LogEvent(new SmokeEvent("periodic-smoke"));
        yield return Until(() => _transport.Bodies.Count == 1);
        Assert.That(Events(_transport.Bodies[0])[0]["build_version"].Value<string>(), Is.EqualTo("periodic-smoke"));
        yield return Complete(_analytics.FlushAsync());
        Assert.That(_transport.Bodies.Count, Is.EqualTo(1), "Accepted events must leave the queue.");
    }

    [UnityTest]
    public IEnumerator TransientFailure_AutomaticallyRetriesSameEnvelope()
    {
        NetworkStatus.ForceOffline = false;
        Assert.That(NetworkStatus.IsOnline, Is.True);
        _transport.FailuresRemaining = 1;
        _analytics = SmokeEnvironment.CreateSystem(_transport, TimeSpan.Zero);
        _analytics.LogEvent(new SmokeEvent("retry-smoke"));
        _analytics.Flush();
        yield return Until(() => _transport.Bodies.Count >= 2);
        Assert.That(_transport.Bodies.Count, Is.EqualTo(2));
        Assert.That(EventId(_transport.Bodies[1]), Is.EqualTo(EventId(_transport.Bodies[0])));
        yield return Complete(_analytics.FlushAsync());
        Assert.That(_transport.Bodies.Count, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator PendingEvent_SurvivesDisposeAndRecreation()
    {
        _analytics = SmokeEnvironment.CreateSystem(_transport, TimeSpan.Zero);
        _analytics.LogEvent(new SmokeEvent("restart-smoke"));
        yield return null;
        Assert.That(_transport.Bodies, Is.Empty, "Offline events must stay queued.");
        _analytics.Dispose();
        _analytics = SmokeEnvironment.CreateSystem(_transport, TimeSpan.Zero);
        yield return Complete(_analytics.FlushAsync());
        Assert.That(_transport.Bodies.Count, Is.EqualTo(1));
        Assert.That(Events(_transport.Bodies[0]).Count, Is.EqualTo(1));
        Assert.That(Events(_transport.Bodies[0])[0]["build_version"].Value<string>(), Is.EqualTo("restart-smoke"));
        yield return Complete(_analytics.FlushAsync());
        Assert.That(_transport.Bodies.Count, Is.EqualTo(1));
    }

    static JArray Events(string body) => (JArray)JObject.Parse(body)["events"];
    static string EventId(string body) => Events(body)[0]["event_id"].Value<string>();

    static IEnumerator Complete(Task task)
    {
        yield return Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    static IEnumerator Until(Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!condition() && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(condition(), Is.True, "Smoke operation timed out after 10 seconds.");
    }
}
