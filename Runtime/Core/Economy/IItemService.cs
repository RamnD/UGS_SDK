using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Сервис предметов игрока (инвентарь — не расходуемые ресурсы, а постоянно хранимые предметы).
/// TItem — проектный enum предметов (например, ItemId).
/// Весь маппинг и логика стоимости делегируется в <see cref="IItemMapper{TItem,TCurrency}"/>.
/// <para>Сбой синхронизации при активной сети → <see cref="InventoryOperationException"/>.</para>
/// </summary>
/// <typeparam name="TItem">Проектный enum с идентификаторами предметов.</typeparam>
public interface IItemService<TItem> where TItem : struct, Enum
{
    /// <summary>
    /// Есть ли предмет у игрока (проверяет локальный кэш, без обращения к серверу).
    /// </summary>
    bool IsOwned(TItem id);

    /// <summary>
    /// Синхронизирует список предметов с сервером.
    /// Если офлайн — загружает последний кэш из PlayerPrefs без исключений.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Покупает предмет: списывает валюту → выдаёт предмет на сервере → обновляет кэш.
    /// <para>
    /// Если списание прошло, но выдача предмета упала — валюта возвращается автоматически (rollback).
    /// </para>
    /// </summary>
    /// <returns>True если покупка успешно завершена.</returns>
    Task<bool> TryPurchaseAsync(TItem id, CancellationToken cancellationToken = default);
}
