using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using GooglePlayGames;
#endif
#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
using Apple.GameKit;
#endif

internal static class PlatformAchievementBridgeFactory
{
    public static IPlatformAchievementBridge Create(PlatformAchievementsOptions options)
    {
        if (options?.Mapper == null)
            return new NullPlatformAchievementBridge();

#if UNITY_ANDROID && !UNITY_EDITOR
        if (options.UseGooglePlayGames)
            return new GooglePlayGamesAchievementBridge(options.Mapper);
#elif UNITY_IOS && !UNITY_EDITOR
        if (options.UseAppleGameCenter)
            return new AppleGameCenterAchievementBridge(options.Mapper, options.ShowAppleCompletionBanner);
#endif
        return new NullPlatformAchievementBridge();
    }
}

internal abstract class PlatformAchievementBridgeBase : IPlatformAchievementBridge
{
    readonly PlatformAchievementSyncState _state;

    protected PlatformAchievementBridgeBase(string prefsKey)
    {
        _state = new PlatformAchievementSyncState(prefsKey);
    }

    public async Task ReportProgressAsync(
        string achievementId,
        double currentProgress,
        double targetProgress,
        CancellationToken cancellationToken = default)
    {
        ValidateAchievementId(achievementId);
        double normalized = NormalizeProgress(currentProgress, targetProgress);

        if (!TryMapPlatformId(achievementId, out string platformId))
            return;

        if (!_state.ShouldReportProgress(achievementId, normalized))
            return;

        if (!CanReportNow())
        {
            _state.EnqueuePendingProgress(achievementId, normalized);
            return;
        }

        bool success = await TryReportPlatformProgressAsync(platformId, normalized, cancellationToken);
        if (success)
            _state.MarkProgressReported(achievementId, normalized);
        else
            _state.EnqueuePendingProgress(achievementId, normalized);
    }

    public async Task ReportUnlockAsync(
        string achievementId,
        CancellationToken cancellationToken = default)
    {
        ValidateAchievementId(achievementId);

        if (!TryMapPlatformId(achievementId, out string platformId))
            return;

        if (!_state.ShouldReportUnlock(achievementId))
            return;

        if (!CanReportNow())
        {
            _state.EnqueuePendingUnlock(achievementId);
            return;
        }

        bool success = await TryReportPlatformProgressAsync(platformId, 1d, cancellationToken);
        if (success)
            _state.MarkUnlockReported(achievementId);
        else
            _state.EnqueuePendingUnlock(achievementId);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanReportNow())
            return;

        foreach (string achievementId in _state.GetPendingUnlockIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryMapPlatformId(achievementId, out string platformId))
            {
                _state.MarkUnlockReported(achievementId);
                continue;
            }

            bool success = await TryReportPlatformProgressAsync(platformId, 1d, cancellationToken);
            if (!success)
                return;

            _state.MarkUnlockReported(achievementId);
        }

        foreach (PendingProgressReport pending in _state.GetPendingProgressReports())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_state.HasPendingUnlock(pending.achievementId))
                continue;

            if (!TryMapPlatformId(pending.achievementId, out string platformId))
            {
                _state.MarkProgressReported(pending.achievementId, pending.normalizedProgress);
                continue;
            }

            bool success = await TryReportPlatformProgressAsync(
                platformId,
                pending.normalizedProgress,
                cancellationToken);
            if (!success)
                return;

            _state.MarkProgressReported(pending.achievementId, pending.normalizedProgress);
        }
    }

    public void ClearLocalCache() => _state.Clear();

    protected abstract bool TryMapPlatformId(string achievementId, out string platformId);
    protected abstract bool CanReportNow();
    protected abstract Task<bool> TryReportPlatformProgressAsync(
        string platformAchievementId,
        double normalizedProgress,
        CancellationToken cancellationToken);

    static void ValidateAchievementId(string achievementId)
    {
        if (string.IsNullOrWhiteSpace(achievementId))
            throw new ArgumentException("Achievement ID must be non-empty.", nameof(achievementId));
    }

    static double NormalizeProgress(double currentProgress, double targetProgress)
    {
        if (currentProgress < 0d)
            throw new ArgumentOutOfRangeException(nameof(currentProgress), "Progress cannot be negative.");
        if (targetProgress < 0d)
            throw new ArgumentOutOfRangeException(nameof(targetProgress), "Target progress cannot be negative.");

        if (targetProgress <= 0d)
            return currentProgress > 0d ? 1d : 0d;

        if (currentProgress <= 0d)
            return 0d;

        double normalized = currentProgress / targetProgress;
        if (normalized < 0d)
            return 0d;
        if (normalized > 1d)
            return 1d;
        return normalized;
    }
}

internal sealed class GooglePlayGamesAchievementBridge : PlatformAchievementBridgeBase
{
    readonly IAchievementPlatformMapper _mapper;

    public GooglePlayGamesAchievementBridge(IAchievementPlatformMapper mapper)
        : base("platform_achievements_google_v1")
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    protected override bool TryMapPlatformId(string achievementId, out string platformId) =>
        _mapper.TryGetGooglePlayAchievementId(achievementId, out platformId)
        && !string.IsNullOrWhiteSpace(platformId);

    protected override bool CanReportNow()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            PlayGamesPlatform.Activate();
            return PlayGamesPlatform.Instance != null && PlayGamesPlatform.Instance.IsAuthenticated();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Achievements.GooglePlay", $"Availability check failed: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    protected override Task<bool> TryReportPlatformProgressAsync(
        string platformAchievementId,
        double normalizedProgress,
        CancellationToken cancellationToken)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        cancellationToken.ThrowIfCancellationRequested();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration ctr = default;
        if (cancellationToken.CanBeCanceled)
            ctr = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        try
        {
            double percent = normalizedProgress * 100d;
            PlayGamesPlatform.Activate();
            PlayGamesPlatform.Instance.ReportProgress(platformAchievementId, percent, success =>
            {
                if (success)
                    AppLog.Info("Achievements.GooglePlay", $"Reported '{platformAchievementId}' = {percent:F2}%.");
                else
                    AppLog.Warn("Achievements.GooglePlay", $"Report failed for '{platformAchievementId}' ({percent:F2}%).");
                tcs.TrySetResult(success);
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Achievements.GooglePlay", $"Report threw for '{platformAchievementId}': {ex.Message}");
            tcs.TrySetResult(false);
        }

        return AwaitAndDisposeAsync(tcs.Task, ctr);
#else
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
#endif
    }

    static async Task<bool> AwaitAndDisposeAsync(Task<bool> task, CancellationTokenRegistration ctr)
    {
        try
        {
            return await task;
        }
        finally
        {
            ctr.Dispose();
        }
    }
}

internal sealed class AppleGameCenterAchievementBridge : PlatformAchievementBridgeBase
{
    readonly IAchievementPlatformMapper _mapper;
    readonly bool _showCompletionBanner;

    public AppleGameCenterAchievementBridge(
        IAchievementPlatformMapper mapper,
        bool showCompletionBanner)
        : base("platform_achievements_apple_v1")
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _showCompletionBanner = showCompletionBanner;
    }

    protected override bool TryMapPlatformId(string achievementId, out string platformId) =>
        _mapper.TryGetAppleGameCenterAchievementId(achievementId, out platformId)
        && !string.IsNullOrWhiteSpace(platformId);

    protected override bool CanReportNow()
    {
#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
        try
        {
            return GKLocalPlayer.Local != null && GKLocalPlayer.Local.IsAuthenticated;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Achievements.GameCenter", $"Availability check failed: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    protected override async Task<bool> TryReportPlatformProgressAsync(
        string platformAchievementId,
        double normalizedProgress,
        CancellationToken cancellationToken)
    {
#if UNITY_IOS && !UNITY_EDITOR && APPLE_GAMEKIT
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var achievement = GKAchievement.Init(platformAchievementId);
            achievement.PercentComplete = normalizedProgress * 100d;
            achievement.ShowCompletionBanner = _showCompletionBanner && normalizedProgress >= 1d;
            await GKAchievement.Report(achievement);
            cancellationToken.ThrowIfCancellationRequested();
            AppLog.Info("Achievements.GameCenter", $"Reported '{platformAchievementId}' = {achievement.PercentComplete:F2}%.");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Achievements.GameCenter", $"Report failed for '{platformAchievementId}': {ex.Message}");
            return false;
        }
#else
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        return false;
#endif
    }
}

internal sealed class PlatformAchievementSyncState
{
    readonly string _prefsKey;
    readonly object _sync = new object();

    public PlatformAchievementSyncState(string prefsKey)
    {
        _prefsKey = prefsKey ?? throw new ArgumentNullException(nameof(prefsKey));
    }

    public bool ShouldReportUnlock(string achievementId)
    {
        lock (_sync)
        {
            State state = LoadUnlocked();
            if (state.reportedUnlocks.Contains(achievementId))
                return false;
            return true;
        }
    }

    public bool ShouldReportProgress(string achievementId, double normalizedProgress)
    {
        lock (_sync)
        {
            State state = LoadUnlocked();
            if (normalizedProgress >= 1d && state.reportedUnlocks.Contains(achievementId))
                return false;

            double reported = GetReportedProgressUnlocked(state, achievementId);
            double pending = GetPendingProgressUnlocked(state, achievementId);
            double knownMax = Math.Max(reported, pending);
            return normalizedProgress > knownMax + 0.0001d;
        }
    }

    public bool HasPendingUnlock(string achievementId)
    {
        lock (_sync)
        {
            return LoadUnlocked().pendingUnlocks.Contains(achievementId);
        }
    }

    public void EnqueuePendingUnlock(string achievementId)
    {
        lock (_sync)
        {
            State state = LoadUnlocked();
            state.pendingUnlocks.Add(achievementId);
            RemovePendingProgressUnlocked(state, achievementId);
            PersistUnlocked(state);
        }
    }

    public void EnqueuePendingProgress(string achievementId, double normalizedProgress)
    {
        lock (_sync)
        {
            State state = LoadUnlocked();
            if (state.pendingUnlocks.Contains(achievementId))
                return;

            UpsertProgressUnlocked(state.pendingProgress, achievementId, normalizedProgress);
            PersistUnlocked(state);
        }
    }

    public void MarkUnlockReported(string achievementId)
    {
        lock (_sync)
        {
            State state = LoadUnlocked();
            state.reportedUnlocks.Add(achievementId);
            UpsertProgressUnlocked(state.reportedProgress, achievementId, 1d);
            state.pendingUnlocks.Remove(achievementId);
            RemovePendingProgressUnlocked(state, achievementId);
            PersistUnlocked(state);
        }
    }

    public void MarkProgressReported(string achievementId, double normalizedProgress)
    {
        lock (_sync)
        {
            State state = LoadUnlocked();
            UpsertProgressUnlocked(state.reportedProgress, achievementId, normalizedProgress);
            RemovePendingProgressUnlocked(state, achievementId);
            if (normalizedProgress >= 1d)
            {
                state.reportedUnlocks.Add(achievementId);
                state.pendingUnlocks.Remove(achievementId);
            }

            PersistUnlocked(state);
        }
    }

    public IReadOnlyList<string> GetPendingUnlockIds()
    {
        lock (_sync)
        {
            State state = LoadUnlocked();
            return state.pendingUnlocks.Count == 0
                ? Array.Empty<string>()
                : state.pendingUnlocks.ToArray();
        }
    }

    public IReadOnlyList<PendingProgressReport> GetPendingProgressReports()
    {
        lock (_sync)
        {
            State state = LoadUnlocked();
            if (state.pendingProgress.Count == 0)
                return Array.Empty<PendingProgressReport>();

            var result = new PendingProgressReport[state.pendingProgress.Count];
            for (int i = 0; i < state.pendingProgress.Count; i++)
            {
                ProgressEntry entry = state.pendingProgress[i];
                result[i] = new PendingProgressReport(entry.achievementId, entry.normalizedProgress);
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            PlayerPrefs.DeleteKey(_prefsKey);
            PlayerPrefs.Save();
        }
    }

    State LoadUnlocked()
    {
        string json = PlayerPrefs.GetString(_prefsKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                State parsed = JsonConvert.DeserializeObject<State>(json);
                if (parsed != null)
                {
                    parsed.reportedUnlocks ??= new HashSet<string>(StringComparer.Ordinal);
                    parsed.reportedProgress ??= new List<ProgressEntry>();
                    parsed.pendingUnlocks ??= new HashSet<string>(StringComparer.Ordinal);
                    parsed.pendingProgress ??= new List<ProgressEntry>();
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Achievements", $"Platform sync state parse failed ({_prefsKey}): {ex.Message}");
            }
        }

        return new State();
    }

    void PersistUnlocked(State state)
    {
        try
        {
            string json = JsonConvert.SerializeObject(state);
            PlayerPrefs.SetString(_prefsKey, json);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Achievements", $"Platform sync state persist failed ({_prefsKey}): {ex.Message}");
        }
    }

    static double GetReportedProgressUnlocked(State state, string achievementId)
    {
        for (int i = 0; i < state.reportedProgress.Count; i++)
        {
            if (string.Equals(state.reportedProgress[i].achievementId, achievementId, StringComparison.Ordinal))
                return state.reportedProgress[i].normalizedProgress;
        }

        return 0d;
    }

    static double GetPendingProgressUnlocked(State state, string achievementId)
    {
        for (int i = 0; i < state.pendingProgress.Count; i++)
        {
            if (string.Equals(state.pendingProgress[i].achievementId, achievementId, StringComparison.Ordinal))
                return state.pendingProgress[i].normalizedProgress;
        }

        return 0d;
    }

    static void RemovePendingProgressUnlocked(State state, string achievementId)
    {
        for (int i = state.pendingProgress.Count - 1; i >= 0; i--)
        {
            if (string.Equals(state.pendingProgress[i].achievementId, achievementId, StringComparison.Ordinal))
                state.pendingProgress.RemoveAt(i);
        }
    }

    static void UpsertProgressUnlocked(List<ProgressEntry> list, string achievementId, double normalizedProgress)
    {
        normalizedProgress = Clamp01(normalizedProgress);
        for (int i = 0; i < list.Count; i++)
        {
            if (!string.Equals(list[i].achievementId, achievementId, StringComparison.Ordinal))
                continue;

            if (normalizedProgress > list[i].normalizedProgress)
                list[i].normalizedProgress = normalizedProgress;
            return;
        }

        list.Add(new ProgressEntry
        {
            achievementId = achievementId,
            normalizedProgress = normalizedProgress,
        });
    }

    static double Clamp01(double value)
    {
        if (value < 0d)
            return 0d;
        if (value > 1d)
            return 1d;
        return value;
    }

    [Serializable]
    sealed class State
    {
        public HashSet<string> reportedUnlocks = new(StringComparer.Ordinal);
        public List<ProgressEntry> reportedProgress = new();
        public HashSet<string> pendingUnlocks = new(StringComparer.Ordinal);
        public List<ProgressEntry> pendingProgress = new();
    }

    [Serializable]
    sealed class ProgressEntry
    {
        public string achievementId;
        public double normalizedProgress;
    }
}

internal readonly struct PendingProgressReport
{
    public string achievementId { get; }
    public double normalizedProgress { get; }

    public PendingProgressReport(string achievementId, double normalizedProgress)
    {
        this.achievementId = achievementId;
        this.normalizedProgress = normalizedProgress;
    }
}
