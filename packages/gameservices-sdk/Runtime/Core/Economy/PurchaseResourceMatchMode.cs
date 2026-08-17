/// <summary>
/// How <see cref="PurchaseCatalogQuery"/> matches resource-id filters against purchase lines.
/// </summary>
public enum PurchaseResourceMatchMode
{
    /// <summary>At least one listed resource id must appear on the purchase.</summary>
    Any,

    /// <summary>Every listed resource id must appear on the purchase.</summary>
    All,
}
