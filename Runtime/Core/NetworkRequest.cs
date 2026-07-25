using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Bounds UGS / HTTP awaits so poor mobile links (high RTT, DPI stalls) cannot hang
/// the game indefinitely. Uses <see cref="Task.WhenAny"/> — the underlying call may
/// continue in the background, but the awaiter unblocks with <see cref="TimeoutException"/>.
/// </summary>
public static class NetworkRequest
{
    /// <summary>Default bound for Economy / Cloud Save / Inventory calls (milliseconds).</summary>
    public const int DefaultTimeoutMs = 10000;

    /// <summary>Longer bound for Auth / native sign-in prompts (milliseconds).</summary>
    public const int AuthTimeoutMs = 30000;

    public static async Task<T> WithTimeout<T>(
        Task<T> task,
        CancellationToken cancellationToken = default,
        int timeoutMs = DefaultTimeoutMs)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        if (timeoutMs <= 0)
            return await task.ConfigureAwait(false);

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delay = Task.Delay(timeoutMs, delayCts.Token);

        Task completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (completed == task)
        {
            delayCts.Cancel();
            return await task.ConfigureAwait(false);
        }

        // Suppress UnobservedTaskException when the abandoned UGS call faults later.
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"UGS request timed out after {timeoutMs}ms.");
    }

    public static async Task WithTimeout(
        Task task,
        CancellationToken cancellationToken = default,
        int timeoutMs = DefaultTimeoutMs)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        if (timeoutMs <= 0)
        {
            await task.ConfigureAwait(false);
            return;
        }

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delay = Task.Delay(timeoutMs, delayCts.Token);

        Task completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (completed == task)
        {
            delayCts.Cancel();
            await task.ConfigureAwait(false);
            return;
        }

        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException($"UGS request timed out after {timeoutMs}ms.");
    }
}
