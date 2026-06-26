using System;

/// <summary>Remote Config operation failure (network, UGS, or misconfiguration).</summary>
public sealed class RemoteConfigOperationException : Exception
{
    public RemoteConfigOperationException(string message) : base(message) { }

    public RemoteConfigOperationException(string message, Exception innerException)
        : base(message, innerException) { }
}
