using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Сервис баланса игрока. TCurrency — проектный enum валюты (например, CurrencyType).
/// Весь маппинг enum → string ID делегируется в <see cref="ICurrencyMapper{TCurrency}"/>.
/// <para>
/// Офлайн-стратегия определяется маппером:
/// Add и Spend (где разрешено маппером) — оптимистичный кэш + очередь; иначе требуется сеть.
/// </para>
/// <para>
/// Сетевая / серверная ошибка → <see cref="InventoryOperationException"/>.
/// Для списания: отсутствие средств (после сверки с сервером) → <see cref="TrySpendCurrencyAsync"/> вернёт false, не исключение.
/// </para>
/// </summary>
/// <typeparam name="TCurrency">Проектный enum с типами валюты.</typeparam>
public interface IInventoryService<TCurrency> where TCurrency : struct, Enum
{
    /// <summary>
    /// Синхронно возвращает кэшированное значение баланса. Безопасно вызывать в Update/UI.
    /// </summary>
    long GetCachedBalance(TCurrency type);

    /// <summary>
    /// Синхронизирует балансы с сервером. Вызывать при старте игры и после reconnect.
    /// Если офлайн — загружает последний известный кэш из PlayerPrefs.
    /// </summary>
    Task RefreshBalancesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Начисляет валюту (например, награда за уровень, просмотр рекламы).
    /// Офлайн-поведение определяется <see cref="ICurrencyMapper{TCurrency}.IsOfflineAllowed"/>.
    /// </summary>
    Task AddCurrencyAsync(TCurrency type, int amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Списывает валюту. Офлайн — если разрешено маппером (оптимистичный кэш + очередь).
    /// Возвращает false если не хватает средств. При рассинхронизации с сервером (HTTP 422) обновляет кэш.
    /// </summary>
    /// <returns>True если сервер подтвердил транзакцию.</returns>
    Task<bool> TrySpendCurrencyAsync(TCurrency type, int amount, CancellationToken cancellationToken = default);
}
