/// <summary>
/// Store-localized metadata for a real-money product, suitable for UI price labels.
/// Populated after a successful store product fetch (Apple / Google via Unity IAP).
/// </summary>
public sealed class RealMoneyProductInfo
{
    public string ProductId { get; set; }

    /// <summary>Price formatted with currency symbol, e.g. "$0.99" or "99₽".</summary>
    public string LocalizedPriceString { get; set; }

    public string LocalizedTitle { get; set; }

    public string LocalizedDescription { get; set; }

    /// <summary>ISO 4217 currency code, e.g. USD, EUR.</summary>
    public string IsoCurrencyCode { get; set; }

    /// <summary>Numeric price in the store currency (major units).</summary>
    public decimal LocalizedPrice { get; set; }

    public bool HasLocalizedPrice =>
        !string.IsNullOrWhiteSpace(LocalizedPriceString);
}
