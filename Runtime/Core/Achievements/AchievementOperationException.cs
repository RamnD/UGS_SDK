using System;

/// <summary>
/// Achievement operation failure (network, serialization, or backend misconfiguration).
/// </summary>
public sealed class AchievementOperationException : Exception
{
    public AchievementOperationException(string message, Exception innerException = null)
        : base(message, innerException) { }
}
