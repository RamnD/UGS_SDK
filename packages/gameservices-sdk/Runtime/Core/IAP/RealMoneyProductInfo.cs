/// <summary>
/// Store-localized metadata for a real-money product, suitable for UI price labels.
/// Populated after a successful store product fetch (Apple / Google via Unity IAP).
/// </summary>
public sealed class RealMoneyProductInfo
{
    /// <summary>Game / Economy product id (matches <see cref="RealMoneyProductDefinition.ProductId"/>).</summary>
    public string ProductId { get; set; }

    /// <summary>Price formatted with currency symbol, e.g. "$0.99" or "99₽".</summary>
    public string LocalizedPriceString { get; set; }

    /// <summary>Store-localized product title for UI.</summary>
    public string LocalizedTitle { get; set; }

    /// <summary>Store-localized product description for UI.</summary>
    public string LocalizedDescription { get; set; }

    /// <summary>ISO 4217 currency code, e.g. USD, EUR.</summary>
    public string IsoCurrencyCode { get; set; }

    /// <summary>Numeric price in the store currency (major units).</summary>
    public decimal LocalizedPrice { get; set; }

    /// <summary>True when <see cref="LocalizedPriceString"/> is non-empty.</summary>
    public bool HasLocalizedPrice =>
        !string.IsNullOrWhiteSpace(LocalizedPriceString);
}
