using System;

/// <summary>
/// Maps consumable items to external service IDs and filters what counts as a consumable.
/// <para>Separate from <see cref="IItemMapper{TItem,TCurrency}"/> — permanent unlocks and consumables are kept apart.</para>
/// </summary>
/// <typeparam name="TItem">Project enum of items.</typeparam>
public interface IConsumableItemMapper<TItem> where TItem : struct, Enum
{
    /// <summary>String Currency ID for the SDK (UGS Dashboard → Economy → Currencies).</summary>
    string ToServiceId(TItem item);

    /// <summary>True for stackable consumables (Shield, Nitro, etc.), not permanent unlocks.</summary>
    bool IsConsumable(TItem item);

    /// <summary>Offline rules for grant/consume (same idea as <see cref="ICurrencyMapper{TCurrency}"/>).</summary>
    bool IsOfflineAllowed(TItem item, InventoryOperation op);
}
