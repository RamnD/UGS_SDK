using System;

/// <summary>Cloud save load or push failure (excluding "no data").</summary>
public sealed class CloudSaveOperationException : Exception
{
    /// <summary>Creates a cloud-save failure with a diagnostic message.</summary>
    /// <param name="message">English diagnostic text for logs (not for UI).</param>
    /// <param name="innerException">Optional underlying provider exception.</param>
    public CloudSaveOperationException(string message, Exception innerException = null) : base(message, innerException) { }
}
