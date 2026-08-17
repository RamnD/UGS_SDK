using System;

/// <summary>
/// Achievement operation failure (network, serialization, or backend misconfiguration).
/// </summary>
public sealed class AchievementOperationException : Exception
{
    /// <summary>Creates an achievement failure with a diagnostic message.</summary>
    /// <param name="message">English diagnostic text for logs (not for UI).</param>
    /// <param name="innerException">Optional underlying provider exception.</param>
    public AchievementOperationException(string message, Exception innerException = null)
        : base(message, innerException) { }
}
