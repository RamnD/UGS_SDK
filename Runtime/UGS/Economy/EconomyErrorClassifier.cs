using System;
using Unity.Services.Economy;

/// <summary>
/// Classifies economy / transport failures so the SDK can decide between
/// durable queue fallback, hard failure, and indeterminate (timeout / abandoned) writes.
/// Prefers typed <see cref="EconomyExceptionReason"/> over message substring matching.
/// </summary>
internal static class EconomyErrorClassifier
{
    public enum FailureKind
    {
        /// <summary>Do not optimistic-queue; surface to caller.</summary>
        Fatal,

        /// <summary>
        /// Safe to fall back to local cache + pending queue
        /// (known-failed before commit, or read-only refresh).
        /// </summary>
        Recoverable,

        /// <summary>
        /// Request may still complete on the server (client timeout / abandoned Task).
        /// Do not blind-queue or refund — reconcile against absolute server state first.
        /// </summary>
        Indeterminate,
    }

    public static FailureKind Classify(Exception exception)
    {
        for (Exception walk = exception; walk != null; walk = walk.InnerException)
        {
            if (walk is OperationCanceledException)
                return FailureKind.Fatal;

            // Client abandoned the await — UGS call may still commit.
            if (walk is TimeoutException)
                return FailureKind.Indeterminate;

            if (walk is System.Net.Sockets.SocketException)
                return FailureKind.Recoverable;

            if (walk is System.Net.Http.HttpRequestException)
                return FailureKind.Recoverable;

            if (walk is EconomyException economyException)
                return ClassifyReason(economyException.Reason);
        }

        return FailureKind.Fatal;
    }

    /// <summary>
    /// True when the failure is likely transient and the operation may safely fall back
    /// to local cache + pending queue (not for abandoned/timeout writes).
    /// </summary>
    public static bool IsRecoverable(Exception exception) =>
        Classify(exception) == FailureKind.Recoverable;

    public static bool IsIndeterminate(Exception exception) =>
        Classify(exception) == FailureKind.Indeterminate;

    static FailureKind ClassifyReason(EconomyExceptionReason reason)
    {
        switch (reason)
        {
            case EconomyExceptionReason.NetworkError:
            case EconomyExceptionReason.RequestTimeOut:
                // UGS may have applied the write before the client saw the timeout.
                return FailureKind.Indeterminate;

            case EconomyExceptionReason.RateLimited:
            case EconomyExceptionReason.BadGateway:
            case EconomyExceptionReason.ServiceUnavailable:
            case EconomyExceptionReason.GatewayTimeout:
            case EconomyExceptionReason.InternalServerError:
                return FailureKind.Recoverable;

            case EconomyExceptionReason.UnprocessableTransaction:
            case EconomyExceptionReason.InvalidArgument:
            case EconomyExceptionReason.Unauthorized:
            case EconomyExceptionReason.Forbidden:
            case EconomyExceptionReason.EntityNotFound:
            case EconomyExceptionReason.Conflict:
            case EconomyExceptionReason.ConfigAssignmentHashInvalid:
            case EconomyExceptionReason.ConfigNotSynced:
            case EconomyExceptionReason.NotImplemented:
                return FailureKind.Fatal;

            case EconomyExceptionReason.Unknown:
            default:
                return FailureKind.Fatal;
        }
    }
}
