/// <summary>
/// Resource kind referenced by a purchase cost or reward line.
/// </summary>
public enum PurchaseResourceKind
{
    /// <summary>Currency resource (Economy type <c>CURRENCY</c>).</summary>
    Currency,

    /// <summary>Inventory item resource (Economy type <c>INVENTORY_ITEM</c>).</summary>
    InventoryItem,

    /// <summary>Unknown or unresolved resource.</summary>
    Unknown,
}
