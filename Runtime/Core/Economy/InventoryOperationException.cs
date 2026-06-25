using System;

/// <summary>
/// Ошибка операции экономики или инвентаря UGS Economy.
/// Недостаток средств по данным клиента или 422 после сверки с сервером — не исключение: <see cref="IInventoryService{TCurrency}.TrySpendCurrencyAsync"/> возвращает false.
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

/// <summary>Причина сбоя операции экономики / очереди офлайн-транзакций.</summary>
public enum InventoryFailureReason
{
    /// <summary>Нет доступа к сети при обязательной онлайн-операции.</summary>
    NetworkUnavailable,

    /// <summary>Операция запрещена офлайн (маппер / политика валюты).</summary>
    OperationNotAllowedOffline,

    /// <summary>Сервер / SDK отклонил транзакцию (кроме ожидаемого недостатка средств после 422-сверки).</summary>
    ProviderRejected,

    /// <summary>Не удалось завершить сброс очереди офлайн-начислений.</summary>
    PendingTransactionsFlushFailed
}
