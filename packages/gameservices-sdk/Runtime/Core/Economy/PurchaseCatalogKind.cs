/// <summary>
/// Economy purchase definition kind exposed by <see cref="IEconomyPurchaseCatalog"/>.
/// </summary>
public enum PurchaseCatalogKind
{
    /// <summary>Virtual purchase (soft currency / free bundles).</summary>
    Virtual,

    /// <summary>Real-money purchase (Apple / Google store products).</summary>
    RealMoney,
}
