/// <summary>
/// Balance operation type. Used in <see cref="ICurrencyMapper{TCurrency}.IsOfflineAllowed"/>
/// to define offline rules at the project level.
/// </summary>
public enum InventoryOperation
{
    /// <summary>Credit currency (reward, real-money store purchase).</summary>
    Add,

    /// <summary>Debit currency (in-game purchase, life spend).</summary>
    Spend
}
