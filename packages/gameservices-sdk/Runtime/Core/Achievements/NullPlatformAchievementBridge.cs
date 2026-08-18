using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// No-op implementation used when platform achievements are disabled,
/// unsupported, or not configured for the current build.
/// </summary>
public sealed class NullPlatformAchievementBridge : IPlatformAchievementBridge
{
    public Task ReportProgressAsync(
        string achievementId,
        double currentProgress,
        double targetProgress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ReportUnlockAsync(
        string achievementId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public void ClearLocalCache()
    {
    }
}
