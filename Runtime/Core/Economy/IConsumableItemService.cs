using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Сервис stackable consumable-предметов (количество, списание, выдача).
/// Не заменяет <see cref="IItemService{TItem}"/> — permanent unlocks остаются там.
/// <para>Сбой синхронизации при активной сети → <see cref="InventoryOperationException"/>.</para>
/// </summary>
/// <typeparam name="TItem">Проектный enum предметов (например, ItemId).</typeparam>
public interface IConsumableItemService<TItem> where TItem : struct, Enum
{
    /// <summary>
    /// Текущее количество из локального кэша (без обращения к серверу).
    /// Безопасно вызывать из Update / UI.
    /// </summary>
    int GetQuantity(TItem id);

    /// <summary>
    /// Синхронизирует количества с сервером.
    /// Если офлайн — загружает последний кэш из PlayerPrefs без исключений.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Списывает <paramref name="amount"/> единиц.
    /// Возвращает false при недостатке, офлайне или отказе провайдера (без исключения для «мягких» сбоев).
    /// </summary>
    Task<bool> TryConsumeAsync(
        TItem id,
        int amount = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выдаёт <paramref name="amount"/> единиц (покупка, награда, dev-grant).
    /// </summary>
    Task<bool> TryGrantAsync(
        TItem id,
        int amount = 1,
        CancellationToken cancellationToken = default);

    /// <summary>Срабатывает после изменения количества (consume, grant, refresh).</summary>
    event Action<TItem, int> OnQuantityChanged;
}
