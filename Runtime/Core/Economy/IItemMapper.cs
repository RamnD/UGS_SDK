using System;

/// <summary>
/// Адаптер между проектным enum предметов и строковым ID для внешних сервисов.
/// Также содержит логику стоимости — единая точка истины для UI и покупки.
/// <para>
/// Каждый проект создаёт одну реализацию и передаёт её в
/// <see cref="UGSItemService{TItem,TCurrency}"/> через конструктор.
/// </para>
/// </summary>
/// <typeparam name="TItem">Проектный enum предметов.</typeparam>
/// <typeparam name="TCurrency">Проектный enum валюты (для стоимости покупки).</typeparam>
/// <example>
/// <code>
/// public class ItemMapper : IItemMapper&lt;ItemId, CurrencyType&gt;
/// {
///     public string ToServiceId(ItemId item) => item.ToUGSId();
///     public int GetCost(ItemId item) => item.GemCost();
///     public CurrencyType GetCostCurrency(ItemId item) => CurrencyType.HardGem;
/// }
/// </code>
/// </example>
public interface IItemMapper<TItem, TCurrency>
    where TItem     : struct, Enum
    where TCurrency : struct, Enum
{
    /// <summary>
    /// Конвертирует проектный enum предмета в строковый Inventory Item ID для SDK.
    /// ДОЛЖЕН совпадать с ID предмета в UGS Economy Dashboard.
    /// </summary>
    string ToServiceId(TItem item);

    /// <summary>Количество единиц валюты, которое стоит предмет.</summary>
    int GetCost(TItem item);

    /// <summary>Тип валюты, которой оплачивается покупка предмета.</summary>
    TCurrency GetCostCurrency(TItem item);
}
