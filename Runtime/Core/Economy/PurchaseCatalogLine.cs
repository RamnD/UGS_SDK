using System;

/// <summary>
/// One cost or reward line on an Economy purchase definition.
/// </summary>
public sealed class PurchaseCatalogLine
{
    /// <summary>Economy resource id (currency or inventory item).</summary>
    public string ResourceId { get; }

    /// <summary>Amount granted or consumed.</summary>
    public long Amount { get; }

    /// <summary>Resolved resource kind from Economy configuration.</summary>
    public PurchaseResourceKind ResourceKind { get; }

    /// <summary>Creates a purchase line snapshot.</summary>
    public PurchaseCatalogLine(string resourceId, long amount, PurchaseResourceKind resourceKind)
    {
        ResourceId = resourceId ?? string.Empty;
        Amount = amount;
        ResourceKind = resourceKind;
    }
}
