using System;

/// <summary>
/// Portable achievement progress snapshot returned by <see cref="IAchievementService"/>.
/// </summary>
public readonly struct AchievementState
{
    /// <summary>Project-defined achievement id (string constant).</summary>
    public string AchievementId   { get; }

    /// <summary>Current progress toward <see cref="TargetProgress"/>.</summary>
    public double CurrentProgress { get; }

    /// <summary>Progress required to unlock (implementation may auto-unlock at this value).</summary>
    public double TargetProgress  { get; }

    /// <summary>True when the achievement has been unlocked.</summary>
    public bool   IsUnlocked      { get; }

    /// <summary>UTC unlock time, or null if still locked.</summary>
    public DateTime? UnlockedAtUtc { get; }

    /// <summary>UTC time of the last local/cloud progress update.</summary>
    public DateTime UpdatedAtUtc  { get; }

    /// <summary>Creates an immutable progress snapshot.</summary>
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
