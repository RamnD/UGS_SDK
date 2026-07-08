using System;

/// <summary>
/// Portable achievement progress snapshot.
/// </summary>
public readonly struct AchievementState
{
    public string AchievementId   { get; }
    public double CurrentProgress { get; }
    public double TargetProgress  { get; }
    public bool   IsUnlocked      { get; }
    public DateTime? UnlockedAtUtc { get; }
    public DateTime UpdatedAtUtc  { get; }

    public AchievementState(
        string achievementId,
        double currentProgress,
        double targetProgress,
        bool isUnlocked,
        DateTime? unlockedAtUtc,
        DateTime updatedAtUtc)
    {
        AchievementId   = achievementId ?? throw new ArgumentNullException(nameof(achievementId));
        CurrentProgress = currentProgress;
        TargetProgress  = targetProgress;
        IsUnlocked      = isUnlocked;
        UnlockedAtUtc   = unlockedAtUtc;
        UpdatedAtUtc    = updatedAtUtc;
    }
}
