using System;

/// <summary>
/// Маппинг consumable-предметов на ID внешнего сервиса и фильтр «что считается consumable».
/// <para>Отделён от <see cref="IItemMapper{TItem,TCurrency}"/> — permanent unlocks и consumables разведены.</para>
/// </summary>
/// <typeparam name="TItem">Проектный enum предметов.</typeparam>
public interface IConsumableItemMapper<TItem> where TItem : struct, Enum
{
    /// <summary>Строковый Currency ID для SDK (UGS Dashboard → Economy → Currencies).</summary>
    string ToServiceId(TItem item);

    /// <summary>True для stackable consumables (Shield, Nitro и т.д.), не для permanent unlocks.</summary>
    bool IsConsumable(TItem item);

    /// <summary>Офлайн-правила для grant/consume (аналог <see cref="ICurrencyMapper{TCurrency}"/>).</summary>
    bool IsOfflineAllowed(TItem item, InventoryOperation op);
}
