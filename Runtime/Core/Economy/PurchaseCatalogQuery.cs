using System.Collections.Generic;

/// <summary>
/// In-memory filter for <see cref="IEconomyPurchaseCatalog.Query"/>.
/// All specified filters must match (AND). Unset filters are ignored.
/// </summary>
public struct PurchaseCatalogQuery
{
    /// <summary>When set, only entries of this kind are returned.</summary>
    public PurchaseCatalogKind? Kind;

    /// <summary>When set, purchase id must contain this substring (case-insensitive).</summary>
    public string IdContains;

    /// <summary>
    /// When set, filters on <see cref="PurchaseCatalogEntry.Rewards"/> resource ids.
    /// </summary>
    public IReadOnlyList<string> RewardResourceIds;

    /// <summary>How <see cref="RewardResourceIds"/> is matched. Defaults to <see cref="PurchaseResourceMatchMode.Any"/>.</summary>
    public PurchaseResourceMatchMode RewardMatch;

    /// <summary>
    /// When set, filters on <see cref="PurchaseCatalogEntry.Costs"/> resource ids (virtual purchases).
    /// </summary>
    public IReadOnlyList<string> CostResourceIds;

    /// <summary>How <see cref="CostResourceIds"/> is matched. Defaults to <see cref="PurchaseResourceMatchMode.Any"/>.</summary>
    public PurchaseResourceMatchMode CostMatch;

    /// <summary>Empty query — returns all cached entries.</summary>
    public static PurchaseCatalogQuery All => default;
}
