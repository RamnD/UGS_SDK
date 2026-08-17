using System;

/// <summary>Remote Config operation failure (network, UGS, or misconfiguration).</summary>
public sealed class RemoteConfigOperationException : Exception
{
    /// <summary>Creates a Remote Config failure with a diagnostic message.</summary>
    public RemoteConfigOperationException(string message) : base(message) { }

    /// <summary>Creates a Remote Config failure wrapping a provider exception.</summary>
    public RemoteConfigOperationException(string message, Exception innerException)
        : base(message, innerException) { }
}
