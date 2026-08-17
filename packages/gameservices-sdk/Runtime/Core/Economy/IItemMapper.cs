using System;

/// <summary>
/// Adapter between the project item enum and string IDs for external services.
/// Also holds cost logic — single source of truth for UI and purchase.
/// <para>
/// Each project implements this once and passes it to
/// <see cref="UGSItemService{TItem,TCurrency}"/> via the constructor.
/// </para>
/// </summary>
/// <typeparam name="TItem">Project enum of items.</typeparam>
/// <typeparam name="TCurrency">Project currency enum (for purchase cost).</typeparam>
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
    /// Converts the project item enum to a string Inventory Item ID for the SDK.
    /// MUST match the item ID in the UGS Economy Dashboard.
    /// </summary>
    string ToServiceId(TItem item);

    /// <summary>Currency units required to buy the item.</summary>
    int GetCost(TItem item);

    /// <summary>Currency type used to pay for the item.</summary>
    TCurrency GetCostCurrency(TItem item);
}
