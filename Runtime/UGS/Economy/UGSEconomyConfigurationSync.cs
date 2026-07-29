using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Economy;
using UnityEngine;

/// <summary>
/// Shared single-flight UGS Economy configuration sync used by virtual purchases and the purchase catalog.
/// </summary>
static class UGSEconomyConfigurationSync
{
    const int DefaultTimeoutMs = 15000;

    static readonly object Gate = new();
    static Task _syncTask;
    static bool _synced;

    /// <summary>True after at least one successful <see cref="SyncAsync"/> in this process.</summary>
    public static bool IsSynced
    {
        get { lock (Gate) return _synced; }
    }

    /// <summary>Forces the next non-force sync to hit the network again (e.g. after ConfigNotSynced).</summary>
    public static void Invalidate()
    {
        lock (Gate)
            _synced = false;
    }

    /// <summary>
    /// Ensures Economy configuration is synced.
    /// When <paramref name="force"/> is false, skips the network call if already synced.
    /// When <paramref name="force"/> is true, always requests a fresh sync (single-flight with other callers).
    /// </summary>
    public static async Task SyncAsync(
        CancellationToken cancellationToken = default,
        bool force = false,
        int timeoutMs = DefaultTimeoutMs)
    {
        Task syncTask;
        lock (Gate)
        {
            if (!force && _synced)
                return;

            if (_syncTask != null && !_syncTask.IsCompleted)
            {
                syncTask = _syncTask;
            }
            else
            {
                syncTask = SyncCoreAsync(cancellationToken, timeoutMs);
                _syncTask = syncTask;
            }
        }

        try
        {
            await syncTask;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (Gate)
            {
                if (ReferenceEquals(_syncTask, syncTask) && syncTask.IsCompleted)
                    _syncTask = null;
            }
        }
    }

    static async Task SyncCoreAsync(CancellationToken cancellationToken, int timeoutMs)
    {
        await NetworkRequest.WithTimeout(
            EconomyService.Instance.Configuration.SyncConfigurationAsync(),
            cancellationToken,
            timeoutMs: timeoutMs);

        lock (Gate)
            _synced = true;

        NetworkStatus.ReportSuccess();
    }
}
