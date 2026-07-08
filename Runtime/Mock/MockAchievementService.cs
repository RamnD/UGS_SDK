using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Mock <see cref="IAchievementService"/> implementation.
/// Stores achievement state in memory only.
/// </summary>
public sealed class MockAchievementService : IAchievementService
{
    private readonly Dictionary<string, AchievementState> _states = new(StringComparer.Ordinal);

    public bool TryGetState(string achievementId, out AchievementState state)
    {
        ValidateAchievementId(achievementId);
        return _states.TryGetValue(achievementId, out state);
    }

    public IReadOnlyCollection<AchievementState> GetAllStates() =>
        _states.Values.ToArray();

    public Task SetProgressAsync(
        string achievementId,
        double currentProgress,
        double targetProgress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateProgress(currentProgress, targetProgress);

        DateTime now = DateTime.UtcNow;
        bool isUnlocked = targetProgress > 0d && currentProgress >= targetProgress;
        DateTime? unlockedAt = ResolveUnlockedAt(achievementId, isUnlocked, now);

        _states[achievementId] = new AchievementState(
            achievementId,
            currentProgress,
            targetProgress,
            isUnlocked,
            unlockedAt,
            now);

        Debug.Log($"[Mock Achievements] SetProgress '{achievementId}': {currentProgress}/{targetProgress}, unlocked={isUnlocked}");
        return Task.CompletedTask;
    }

    public Task IncrementProgressAsync(
        string achievementId,
        double deltaProgress,
        double targetProgress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAchievementId(achievementId);
        ValidateProgress(deltaProgress, targetProgress, allowNegativeCurrent: false);

        double current = deltaProgress;
        DateTime? unlockedAt = null;
        if (_states.TryGetValue(achievementId, out AchievementState existing))
        {
            current += existing.CurrentProgress;
            unlockedAt = existing.UnlockedAtUtc;
        }

        bool isUnlocked = targetProgress > 0d && current >= targetProgress;
        DateTime now = DateTime.UtcNow;
        if (isUnlocked && !unlockedAt.HasValue)
            unlockedAt = now;

        _states[achievementId] = new AchievementState(
            achievementId,
            current,
            targetProgress,
            isUnlocked,
            unlockedAt,
            now);

        Debug.Log($"[Mock Achievements] IncrementProgress '{achievementId}': +{deltaProgress}, total={current}/{targetProgress}, unlocked={isUnlocked}");
        return Task.CompletedTask;
    }

    public Task UnlockAsync(string achievementId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateAchievementId(achievementId);

        DateTime now = DateTime.UtcNow;
        AchievementState next = _states.TryGetValue(achievementId, out AchievementState existing)
            ? new AchievementState(
                achievementId,
                Math.Max(existing.CurrentProgress, existing.TargetProgress),
                existing.TargetProgress,
                true,
                existing.UnlockedAtUtc ?? now,
                now)
            : new AchievementState(achievementId, 1d, 1d, true, now, now);

        _states[achievementId] = next;
        Debug.Log($"[Mock Achievements] Unlock '{achievementId}'.");
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Debug.Log("[Mock Achievements] Flush (mock, nothing to send).");
        return Task.CompletedTask;
    }

    static void ValidateAchievementId(string achievementId)
    {
        if (string.IsNullOrWhiteSpace(achievementId))
            throw new ArgumentException("Achievement ID must be non-empty.", nameof(achievementId));
    }

    static void ValidateProgress(double currentProgress, double targetProgress, bool allowNegativeCurrent = true)
    {
        if (!allowNegativeCurrent && currentProgress < 0d)
            throw new ArgumentOutOfRangeException(nameof(currentProgress), "Progress delta cannot be negative.");
        if (allowNegativeCurrent && currentProgress < 0d)
            throw new ArgumentOutOfRangeException(nameof(currentProgress), "Progress cannot be negative.");
        if (targetProgress < 0d)
            throw new ArgumentOutOfRangeException(nameof(targetProgress), "Target progress cannot be negative.");
    }

    DateTime? ResolveUnlockedAt(string achievementId, bool isUnlocked, DateTime now)
    {
        if (!isUnlocked)
            return _states.TryGetValue(achievementId, out AchievementState existing) ? existing.UnlockedAtUtc : null;

        return _states.TryGetValue(achievementId, out AchievementState unlockedExisting)
            ? unlockedExisting.UnlockedAtUtc ?? now
            : now;
    }
}
