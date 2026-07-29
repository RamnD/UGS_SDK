using System;
using System.Collections.Generic;

/// <summary>
/// Read-only snapshot of one Economy virtual or real-money purchase definition.
/// </summary>
public sealed class PurchaseCatalogEntry
{
    /// <summary>Economy purchase id (case-sensitive).</summary>
    public string Id { get; }

    /// <summary>Display name from the Economy dashboard.</summary>
    public string Name { get; }

    /// <summary>Virtual or real-money purchase kind.</summary>
    public PurchaseCatalogKind Kind { get; }

    /// <summary>Costs (virtual purchases only; empty for real-money purchases).</summary>
    public IReadOnlyList<PurchaseCatalogLine> Costs { get; }

    /// <summary>Rewards granted by the purchase.</summary>
    public IReadOnlyList<PurchaseCatalogLine> Rewards { get; }

    /// <summary>Apple App Store product id (real-money purchases only).</summary>
    public string AppleStoreId { get; }

    /// <summary>Google Play product id (real-money purchases only).</summary>
    public string GoogleStoreId { get; }

    /// <summary>
    /// Raw Custom Data JSON from the Economy dashboard (empty when unset).
    /// Games parse this for section tags, sort keys, badges, etc.
    /// </summary>
    public string CustomDataJson { get; }

    /// <summary>Creates a catalog entry snapshot.</summary>
    public PurchaseCatalogEntry(
        string id,
        string name,
        PurchaseCatalogKind kind,
        IReadOnlyList<PurchaseCatalogLine> costs,
        IReadOnlyList<PurchaseCatalogLine> rewards,
        string appleStoreId = null,
        string googleStoreId = null,
        string customDataJson = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? string.Empty;
        Kind = kind;
        Costs = costs ?? Array.Empty<PurchaseCatalogLine>();
        Rewards = rewards ?? Array.Empty<PurchaseCatalogLine>();
        AppleStoreId = appleStoreId ?? string.Empty;
        GoogleStoreId = googleStoreId ?? string.Empty;
        CustomDataJson = customDataJson ?? string.Empty;
    }
}
