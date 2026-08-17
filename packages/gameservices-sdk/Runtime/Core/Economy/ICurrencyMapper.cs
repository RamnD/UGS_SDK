using System;

/// <summary>
/// Adapter between the project currency enum and string IDs for external services.
/// <para>
/// Each project implements this once and passes it to
/// <see cref="UGSEconomyService{TCurrency}"/> via the constructor.
/// </para>
/// </summary>
/// <typeparam name="TCurrency">Project enum of currency types.</typeparam>
/// <example>
/// <code>
/// public class CurrencyMapper : ICurrencyMapper&lt;CurrencyType&gt;
/// {
///     public string ToServiceId(CurrencyType c) => c.ToUGSId(); // project extension method
///     public bool IsOfflineAllowed(CurrencyType c, InventoryOperation op) => c.IsOfflineAllowed(op);
/// }
/// </code>
/// </example>
public interface ICurrencyMapper<TCurrency> where TCurrency : struct, Enum
{
    /// <summary>
    /// Converts the project enum to a string ID for the SDK.
    /// MUST match the resource ID in the backend (UGS Dashboard, etc.).
    /// </summary>
    string ToServiceId(TCurrency currency);

    /// <summary>
    /// Whether this operation may apply locally without a successful server round-trip
    /// (true offline, or recoverable transport failure while seemingly online).
    /// Single source of truth — do not duplicate rules elsewhere.
    /// </summary>
    bool IsOfflineAllowed(TCurrency currency, InventoryOperation op);
}
