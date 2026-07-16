using System;

/// <summary>
/// Economy or inventory operation failure from UGS Economy.
/// Spend soft-failures (insufficient funds, recoverable network when offline spend is
/// disallowed) are not exceptions: <see cref="IInventoryService{TCurrency}.TrySpendCurrencyAsync"/> returns false.
/// Recoverable Add/Spend failures for mapper-allowed currencies are queued locally and also do not throw.
/// </summary>
public sealed class InventoryOperationException : Exception
{
    /// <inheritdoc cref="InventoryFailureReason"/>
    public InventoryFailureReason Reason { get; }

    public InventoryOperationException(InventoryFailureReason reason, string message = null, Exception innerException = null)
        : base(message ?? reason.ToString(), innerException)
    {
        Reason = reason;
    }
}

/// <summary>Reason an economy operation or offline transaction queue flush failed.</summary>
public enum InventoryFailureReason
{
    /// <summary>No network for an operation that requires online.</summary>
    NetworkUnavailable,

    /// <summary>Operation not allowed offline (mapper / currency policy).</summary>
    OperationNotAllowedOffline,

    /// <summary>Server / SDK rejected the transaction (except expected insufficient funds after 422 reconciliation).</summary>
    ProviderRejected,

    /// <summary>Failed to flush the offline transaction queue (non-recoverable provider error).</summary>
    PendingTransactionsFlushFailed
}
