using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Central refresh hub: games and the SDK register per-service refresh callbacks.
/// <list type="bullet">
/// <item><see cref="RefreshAsync"/> — game-driven (all services or one).</item>
/// <item>On <see cref="NetworkStatus.IsOnlineChanged"/> → true — SDK auto-refreshes all
/// registered handlers (critical reconnect path).</item>
/// </list>
/// Economy / Items / CloudSave are project-typed; register them from the game bootstrap.
/// </summary>
public static class GameServicesSync
{
    static readonly object Gate = new object();
    static readonly Dictionary<GameServiceId, Func<CancellationToken, Task>> Handlers = new();
    static bool _subscribedToNetwork;
    static Task _inFlightAll;
    static int _allGeneration;

    /// <summary>
    /// Registers or replaces the refresh handler for <paramref name="service"/>.
    /// Pass <c>null</c> to unregister.
    /// </summary>
    public static void Register(GameServiceId service, Func<CancellationToken, Task> refresh)
    {
        lock (Gate)
        {
            if (refresh == null)
                Handlers.Remove(service);
            else
                Handlers[service] = refresh;

            EnsureNetworkSubscriptionUnlocked();
        }
    }

    /// <summary>Removes a previously registered handler.</summary>
    public static void Unregister(GameServiceId service) => Register(service, null);

    /// <summary>
    /// Refreshes one service, or all registered services when <paramref name="service"/> is null.
    /// Concurrent full refreshes coalesce (callers await the same in-flight task).
    /// </summary>
    public static Task RefreshAsync(
        GameServiceId? service = null,
        CancellationToken cancellationToken = default)
    {
        if (service.HasValue)
            return RefreshOneAsync(service.Value, cancellationToken);

        return RefreshAllAsync(cancellationToken);
    }

    static async Task RefreshOneAsync(GameServiceId service, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task> handler;
        lock (Gate)
        {
            if (!Handlers.TryGetValue(service, out handler) || handler == null)
            {
                Debug.LogWarning($"[SDK][Sync] No refresh handler registered for {service}.");
                return;
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SDK][Sync] Refresh {service} failed: {ex.Message}");
            throw;
        }
    }

    static Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        lock (Gate)
        {
            if (_inFlightAll != null && !_inFlightAll.IsCompleted)
                return AwaitExistingAllAsync(_inFlightAll, cancellationToken);

            int generation = ++_allGeneration;
            Task run = RunAllAsync(generation, cancellationToken);
            _inFlightAll = run;
            return run;
        }
    }

    static async Task AwaitExistingAllAsync(Task existing, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration _ = cancellationToken.Register(() => { /* observe cancel after */ });
        await existing;
        cancellationToken.ThrowIfCancellationRequested();
    }

    static async Task RunAllAsync(int generation, CancellationToken cancellationToken)
    {
        List<KeyValuePair<GameServiceId, Func<CancellationToken, Task>>> snapshot;
        lock (Gate)
        {
            snapshot = new List<KeyValuePair<GameServiceId, Func<CancellationToken, Task>>>(Handlers);
        }

        try
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GameServiceId id = snapshot[i].Key;
                Func<CancellationToken, Task> handler = snapshot[i].Value;
                if (handler == null)
                    continue;

                try
                {
                    await handler(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One service must not abort the reconnect hub for the rest.
                    Debug.LogWarning($"[SDK][Sync] Refresh {id} failed: {ex.Message}");
                }
            }
        }
        finally
        {
            lock (Gate)
            {
                if (_allGeneration == generation)
                    _inFlightAll = null;
            }
        }
    }

    static void EnsureNetworkSubscriptionUnlocked()
    {
        if (_subscribedToNetwork)
            return;

        NetworkStatus.IsOnlineChanged += OnOnlineChanged;
        _subscribedToNetwork = true;
    }

    static void OnOnlineChanged(bool isOnline)
    {
        if (!isOnline)
            return;

        Debug.Log("[SDK][Sync] Online restored — refreshing registered services.");
        _ = RefreshAllAsync(CancellationToken.None);
    }
}
