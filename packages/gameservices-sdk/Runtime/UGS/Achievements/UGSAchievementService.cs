using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.CloudSave;
using UnityEngine;

/// <summary>
/// <see cref="IAchievementService"/> backed by UGS Cloud Save.
/// Stores all achievement state as a single JSON payload under one Cloud Save key.
/// </summary>
public sealed class UGSAchievementService : IAchievementService
{
    private const string CloudSaveKey = "__ramnd_achievements_v1";
    private const string LocalCachePrefsKey = "achievements_local_cache_v1";

    private readonly Dictionary<string, AchievementStateData> _states = new(StringComparer.Ordinal);
    private bool _isLoaded;
    /// <summary>True after a successful online LoadAll (including "no payload"). Flush must not overwrite cloud without this.</summary>
    private bool _hasCloudBaseline;
    private bool _isDirty;

    private readonly object _loadGate = new object();
    private readonly object _flushGate = new object();
    private Task _loadTask;
    private Task _flushTask;

    /// <summary>
    /// Loads achievement state from Cloud Save (or local cache when offline) before first use.
    /// Called automatically by the builder when achievements are enabled; safe to call again.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load await.</param>
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void ClearLocalCache()
    {
        _states.Clear();
        _isLoaded = false;
        _hasCloudBaseline = false;
        _isDirty = false;
        lock (_loadGate) _loadTask = null;
        lock (_flushGate) _flushTask = null;
        PlayerPrefs.DeleteKey(LocalCachePrefsKey);
        PlayerPrefs.Save();
        AppLog.Info("Achievements", "ClearLocalCache — in-memory state wiped.");
    }

    /// <inheritdoc/>
    public bool TryGetState(string achievementId, out AchievementState state)
    {
        ValidateAchievementId(achievementId);

        if (_states.TryGetValue(achievementId, out AchievementStateData data))
        {
            state = data.ToPublicState(achievementId);
            return true;
        }

        state = default;
        return false;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<AchievementState> GetAllStates() =>
        _states
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => kvp.Value.ToPublicState(kvp.Key))
            .ToArray();

    /// <inheritdoc/>
    public async Task SetProgressAsync(
        string achievementId,
        double currentProgress,
        double targetProgress,
        CancellationToken cancellationToken = default)
    {
        ValidateAchievementId(achievementId);
        ValidateProgress(currentProgress, targetProgress);
        await EnsureLoadedAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;
        AchievementStateData next = _states.TryGetValue(achievementId, out AchievementStateData existing)
            ? existing
            : new AchievementStateData();

        next.currentProgress = currentProgress;
        next.targetProgress  = targetProgress;
        next.updatedAtUtc    = now;

        if (targetProgress > 0d && currentProgress >= targetProgress)
        {
            next.isUnlocked = true;
            next.unlockedAtUtc ??= now;
        }

        _states[achievementId] = next;
        _isDirty = true;
        PersistLocalCacheToPrefs();

        AppLog.Info("Achievements", $"SetProgress '{achievementId}': {currentProgress}/{targetProgress}, unlocked={next.isUnlocked}");
        await FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task IncrementProgressAsync(
        string achievementId,
        double deltaProgress,
        double targetProgress,
        CancellationToken cancellationToken = default)
    {
        ValidateAchievementId(achievementId);
        ValidateProgress(deltaProgress, targetProgress, allowNegativeCurrent: false);
        await EnsureLoadedAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;
        AchievementStateData next = _states.TryGetValue(achievementId, out AchievementStateData existing)
            ? existing
            : new AchievementStateData();

        next.currentProgress += deltaProgress;
        next.targetProgress   = targetProgress;
        next.updatedAtUtc     = now;

        if (targetProgress > 0d && next.currentProgress >= targetProgress)
        {
            next.isUnlocked = true;
            next.unlockedAtUtc ??= now;
        }

        _states[achievementId] = next;
        _isDirty = true;
        PersistLocalCacheToPrefs();

        AppLog.Info("Achievements", $"IncrementProgress '{achievementId}': +{deltaProgress}, total={next.currentProgress}/{targetProgress}, unlocked={next.isUnlocked}");
        await FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UnlockAsync(string achievementId, CancellationToken cancellationToken = default)
    {
        ValidateAchievementId(achievementId);
        await EnsureLoadedAsync(cancellationToken);

        DateTime now = DateTime.UtcNow;
        AchievementStateData next = _states.TryGetValue(achievementId, out AchievementStateData existing)
            ? existing
            : new AchievementStateData { targetProgress = 1d };

        next.isUnlocked      = true;
        next.unlockedAtUtc ??= now;
        next.updatedAtUtc     = now;
        next.currentProgress  = Math.Max(next.currentProgress, next.targetProgress > 0d ? next.targetProgress : 1d);
        if (next.targetProgress <= 0d)
            next.targetProgress = 1d;

        _states[achievementId] = next;
        _isDirty = true;
        PersistLocalCacheToPrefs();

        AppLog.Info("Achievements", $"Unlock '{achievementId}'.");
        await FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task flush;
        lock (_flushGate)
        {
            if (_flushTask != null && !_flushTask.IsCompleted)
                flush = _flushTask;
            else
            {
                flush = FlushCoreAsync(cancellationToken);
                _flushTask = flush;
            }
        }

        try
        {
            await flush;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_flushGate)
            {
                if (ReferenceEquals(_flushTask, flush) && flush.IsCompleted)
                    _flushTask = null;
            }
        }
    }

    async Task FlushCoreAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        if (!_isDirty)
            return;

        if (!NetworkStatus.IsOnline)
        {
            AppLog.Warn("Achievements", "Offline — keeping pending local achievement state in memory.");
            return;
        }

        if (!_hasCloudBaseline)
        {
            await EnsureCloudBaselineAsync(cancellationToken);
            if (!_hasCloudBaseline)
            {
                AppLog.Warn("Achievements", "Flush skipped — cloud baseline unavailable; local dirty kept in memory.");
                return;
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new Dictionary<string, AchievementStateData>(StringComparer.Ordinal);
            foreach (var kvp in _states)
            {
                AchievementStateData src = kvp.Value;
                snapshot[kvp.Key] = new AchievementStateData
                {
                    currentProgress = src.currentProgress,
                    targetProgress  = src.targetProgress,
                    isUnlocked      = src.isUnlocked,
                    unlockedAtUtc   = src.unlockedAtUtc,
                    updatedAtUtc    = src.updatedAtUtc
                };
            }

            var payload = new AchievementStateCollection { items = snapshot };
            string json = JsonConvert.SerializeObject(payload);
            await NetworkRequest.WithTimeout(
                CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object>
                {
                    [CloudSaveKey] = json
                }),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            NetworkStatus.ReportSuccess();
            _isDirty = false;
            AppLog.Info("Achievements", $"Flushed {snapshot.Count} achievements to Cloud Save.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableTransport(ex))
        {
            NetworkStatus.ReportFailure();
            AppLog.Warn("Achievements", $"Flush failed (recoverable transport): {ex.Message} — keeping dirty local state.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Achievements", $"Flush failed: {ex.Message}");
            throw new AchievementOperationException("Failed to flush achievements to Cloud Save.", ex);
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
            return;

        Task load;
        lock (_loadGate)
        {
            if (_loadTask != null && !_loadTask.IsCompleted)
                load = _loadTask;
            else
            {
                load = LoadCoreAsync(cancellationToken);
                _loadTask = load;
            }
        }

        try
        {
            await load;
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_loadGate)
            {
                if (ReferenceEquals(_loadTask, load) && load.IsCompleted)
                    _loadTask = null;
            }
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        LoadLocalCacheFromPrefs();

        if (!NetworkStatus.IsOnline)
        {
            _isLoaded = true;
            _hasCloudBaseline = false;
            AppLog.Warn("Achievements", "Offline during warmup — local cache only; flush deferred until cloud baseline loads.");
            return;
        }

        try
        {
            await LoadCloudBaselineMergingLocalAsync(cancellationToken);
            _isLoaded = true;
        }
        catch (OperationCanceledException)
        {
            _isLoaded = false;
            _hasCloudBaseline = false;
            throw;
        }
        catch (Exception ex) when (IsRecoverableTransport(ex))
        {
            NetworkStatus.ReportFailure();
            _isLoaded = true;
            _hasCloudBaseline = false;
            AppLog.Warn("Achievements", $"Warmup cloud load failed (recoverable): {ex.Message} — using local cache.");
        }
        catch (Exception ex)
        {
            _isLoaded = false;
            _hasCloudBaseline = false;
            AppLog.Error("Achievements", $"Warmup failed: {ex.Message}");
            throw new AchievementOperationException("Failed to load achievements from Cloud Save.", ex);
        }
    }

    private async Task EnsureCloudBaselineAsync(CancellationToken cancellationToken)
    {
        if (_hasCloudBaseline)
            return;

        if (!NetworkStatus.IsOnline)
            return;

        try
        {
            await LoadCloudBaselineMergingLocalAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Achievements", $"Cloud baseline load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads cloud payload, then re-applies any in-memory local keys on top (local wins per key).
    /// </summary>
    private async Task LoadCloudBaselineMergingLocalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localOverlay = new Dictionary<string, AchievementStateData>(_states, StringComparer.Ordinal);

        var items = await NetworkRequest.WithTimeout(
            CloudSaveService.Instance.Data.Player.LoadAllAsync(),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        NetworkStatus.ReportSuccess();

        _states.Clear();
        if (items.TryGetValue(CloudSaveKey, out var item))
        {
            string json = item.Value.GetAs<string>();
            var payload = JsonConvert.DeserializeObject<AchievementStateCollection>(json);
            if (payload?.items != null)
            {
                foreach (var kvp in payload.items)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null)
                        _states[kvp.Key] = kvp.Value;
                }
            }
        }
        else
        {
            AppLog.Info("Achievements", "No cloud payload found — starting with empty state.");
        }

        foreach (var kvp in localOverlay)
            _states[kvp.Key] = kvp.Value;

        _hasCloudBaseline = true;
        PersistLocalCacheToPrefs();
        AppLog.Info("Achievements", $"Loaded cloud baseline ({_states.Count} achievements, local overlay keys={localOverlay.Count}).");
    }

    // ── Local PlayerPrefs cache ──────────────────────────────────────────────

    void PersistLocalCacheToPrefs()
    {
        try
        {
            var payload = new AchievementStateCollection { items = new Dictionary<string, AchievementStateData>(_states, StringComparer.Ordinal) };
            string json = JsonConvert.SerializeObject(payload);
            PlayerPrefs.SetString(LocalCachePrefsKey, json);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Achievements", $"Failed to persist local cache: {ex.Message}");
        }
    }

    void LoadLocalCacheFromPrefs()
    {
        if (_states.Count > 0)
            return;

        string json = PlayerPrefs.GetString(LocalCachePrefsKey, "");
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            var payload = JsonConvert.DeserializeObject<AchievementStateCollection>(json);
            if (payload?.items != null)
            {
                foreach (var kvp in payload.items)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null)
                        _states[kvp.Key] = kvp.Value;
                }

                if (_states.Count > 0)
                    _isDirty = true;

                AppLog.Info("Achievements", $"Restored {_states.Count} achievements from local cache.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Achievements", $"Failed to parse local cache: {ex.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static bool IsRecoverableTransport(Exception exception)
    {
        for (Exception walk = exception; walk != null; walk = walk.InnerException)
        {
            if (walk is OperationCanceledException)
                return false;
            if (walk is TimeoutException)
                return true;
            if (walk is System.Net.Sockets.SocketException)
                return true;
            if (walk is System.Net.Http.HttpRequestException)
                return true;
        }

        return false;
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

    [Serializable]
    sealed class AchievementStateCollection
    {
        public Dictionary<string, AchievementStateData> items = new(StringComparer.Ordinal);
    }

    [Serializable]
    sealed class AchievementStateData
    {
        public double currentProgress;
        public double targetProgress;
        public bool isUnlocked;
        public DateTime? unlockedAtUtc;
        public DateTime updatedAtUtc;

        public AchievementState ToPublicState(string achievementId) =>
            new AchievementState(
                achievementId,
                currentProgress,
                targetProgress,
                isUnlocked,
                unlockedAtUtc,
                updatedAtUtc);
    }
}
