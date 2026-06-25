using System;

/// <summary>
/// Economy or inventory operation failure from UGS Economy.
/// Insufficient funds on the client or HTTP 422 after server reconciliation is not an exception: <see cref="IInventoryService{TCurrency}.TrySpendCurrencyAsync"/> returns false.
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

    /// <summary>Failed to flush the offline credit transaction queue.</summary>
    PendingTransactionsFlushFailed
}
