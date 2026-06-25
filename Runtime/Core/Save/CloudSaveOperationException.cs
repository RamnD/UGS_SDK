using System;

/// <summary>Cloud save load or push failure (excluding "no data").</summary>
public sealed class CloudSaveOperationException : Exception
{
    public CloudSaveOperationException(string message, Exception innerException = null) : base(message, innerException) { }
}
