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

    private readonly Dictionary<string, AchievementStateData> _states = new(StringComparer.Ordinal);
    private bool _isLoaded;
    /// <summary>True after a successful online LoadAll (including "no payload"). Flush must not overwrite cloud without this.</summary>
    private bool _hasCloudBaseline;
    private bool _isDirty;
    private Task _loadTask;

    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
    }

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

    public IReadOnlyCollection<AchievementState> GetAllStates() =>
        _states
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp => kvp.Value.ToPublicState(kvp.Key))
            .ToArray();

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

        Debug.Log($"[Achievements] SetProgress '{achievementId}': {currentProgress}/{targetProgress}, unlocked={next.isUnlocked}");
        await FlushAsync(cancellationToken);
    }

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

        Debug.Log($"[Achievements] IncrementProgress '{achievementId}': +{deltaProgress}, total={next.currentProgress}/{targetProgress}, unlocked={next.isUnlocked}");
        await FlushAsync(cancellationToken);
    }

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

        Debug.Log($"[Achievements] Unlock '{achievementId}'.");
        await FlushAsync(cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        if (!_isDirty)
            return;

        if (!NetworkStatus.IsOnline)
        {
            Debug.LogWarning("[Achievements] Offline — keeping pending local achievement state in memory.");
            return;
        }

        // Never overwrite cloud from an empty/partial cache that never loaded successfully.
        if (!_hasCloudBaseline)
        {
            await EnsureCloudBaselineAsync(cancellationToken);
            if (!_hasCloudBaseline)
            {
                Debug.LogWarning("[Achievements] Flush skipped — cloud baseline unavailable; local dirty kept in memory.");
                return;
            }
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Snapshot to avoid concurrent mutation during serialize.
            var snapshot = new Dictionary<string, AchievementStateData>(_states, StringComparer.Ordinal);
            var payload = new AchievementStateCollection { items = snapshot };
            string json = JsonConvert.SerializeObject(payload);
            await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object>
            {
                [CloudSaveKey] = json
            });
            cancellationToken.ThrowIfCancellationRequested();

            _isDirty = false;
            Debug.Log($"[Achievements] Flushed {snapshot.Count} achievements to Cloud Save.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Achievements] Flush failed: {ex.Message}");
            throw new AchievementOperationException("Failed to flush achievements to Cloud Save.", ex);
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
            return;

        if (_loadTask != null)
        {
            await _loadTask;
            return;
        }

        _loadTask = LoadCoreAsync(cancellationToken);
        try
        {
            await _loadTask;
        }
        finally
        {
            _loadTask = null;
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!NetworkStatus.IsOnline)
        {
            // Local-only: allow in-memory progress, but do not claim a cloud baseline (blocks wipe-on-flush).
            _isLoaded = true;
            _hasCloudBaseline = false;
            Debug.LogWarning("[Achievements] Offline during warmup — local cache only; flush deferred until cloud baseline loads.");
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
        catch (Exception ex)
        {
            _isLoaded = false;
            _hasCloudBaseline = false;
            Debug.LogError($"[Achievements] Warmup failed: {ex.Message}");
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
            Debug.LogWarning($"[Achievements] Cloud baseline load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads cloud payload, then re-applies any in-memory local keys on top (local wins per key).
    /// </summary>
    private async Task LoadCloudBaselineMergingLocalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localOverlay = new Dictionary<string, AchievementStateData>(_states, StringComparer.Ordinal);

        var items = await CloudSaveService.Instance.Data.Player.LoadAllAsync();
        cancellationToken.ThrowIfCancellationRequested();

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
            Debug.Log("[Achievements] No cloud payload found — starting with empty state.");
        }

        foreach (var kvp in localOverlay)
            _states[kvp.Key] = kvp.Value;

        _hasCloudBaseline = true;
        Debug.Log($"[Achievements] Loaded cloud baseline ({_states.Count} achievements, local overlay keys={localOverlay.Count}).");
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
