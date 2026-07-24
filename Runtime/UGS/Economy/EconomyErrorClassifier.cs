using System;
using Unity.Services.Economy;

/// <summary>
/// Classifies economy / transport failures so the SDK can decide between
/// durable queue fallback and hard failure.
/// Prefers typed <see cref="EconomyExceptionReason"/> over message substring matching.
/// </summary>
internal static class EconomyErrorClassifier
{
    /// <summary>
    /// True when the failure is likely transient (timeout, connectivity, selected 5xx)
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
                return IsRecoverableReason(economyException.Reason);
        }

        return false;
    }

    static bool IsRecoverableReason(EconomyExceptionReason reason)
    {
        switch (reason)
        {
            case EconomyExceptionReason.NetworkError:
            case EconomyExceptionReason.RequestTimeOut:
            case EconomyExceptionReason.RateLimited:
            case EconomyExceptionReason.BadGateway:
            case EconomyExceptionReason.ServiceUnavailable:
            case EconomyExceptionReason.GatewayTimeout:
                return true;

            // Client / auth / validation / conflict — do not optimistic-queue.
            case EconomyExceptionReason.UnprocessableTransaction:
            case EconomyExceptionReason.InvalidArgument:
            case EconomyExceptionReason.Unauthorized:
            case EconomyExceptionReason.Forbidden:
            case EconomyExceptionReason.EntityNotFound:
            case EconomyExceptionReason.Conflict:
            case EconomyExceptionReason.ConfigAssignmentHashInvalid:
            case EconomyExceptionReason.ConfigNotSynced:
            case EconomyExceptionReason.NotImplemented:
                return false;

            // 500: often transient, but not "all http 5xx" via substring — typed only.
            case EconomyExceptionReason.InternalServerError:
                return true;

            case EconomyExceptionReason.Unknown:
            default:
                return false;
        }
    }
}
