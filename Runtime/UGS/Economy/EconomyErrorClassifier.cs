using System;
using Unity.Services.Economy;

/// <summary>
/// Classifies economy / transport failures so the SDK can decide between
/// durable queue fallback and hard failure.
/// </summary>
internal static class EconomyErrorClassifier
{
    /// <summary>
    /// True when the failure is likely transient (timeout, connectivity, 5xx-style transport)
    /// and the operation may safely fall back to local cache + pending queue.
    /// </summary>
    public static bool IsRecoverable(Exception exception)
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

            if (walk is EconomyException economyException)
            {
                // Server confirmed insufficient funds / invalid transaction — not a network blip.
                if (economyException.Reason == EconomyExceptionReason.UnprocessableTransaction)
                    return false;

                if (economyException.Reason == EconomyExceptionReason.Unknown)
                    return LooksLikeTransport(economyException.Message);
            }

            if (LooksLikeTransport(walk.Message))
                return true;
        }

        return false;
    }

    static bool LooksLikeTransport(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        return Contains(message, "timeout")
            || Contains(message, "timed out")
            || Contains(message, "network")
            || Contains(message, "connection")
            || Contains(message, "unreachable")
            || Contains(message, "temporarily")
            || Contains(message, "unavailable")
            || Contains(message, "transport")
            || Contains(message, "dns")
            || Contains(message, "socket")
            || Contains(message, "http 5")
            || Contains(message, "503")
            || Contains(message, "502")
            || Contains(message, "504");
    }

    static bool Contains(string haystack, string needle) =>
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
