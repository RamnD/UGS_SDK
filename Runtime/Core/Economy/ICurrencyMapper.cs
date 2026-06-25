using System;

/// <summary>
/// Адаптер между проектным enum валюты и строковым ID для внешних сервисов.
/// <para>
/// Каждый проект создаёт одну реализацию этого интерфейса и передаёт её в
/// <see cref="UGSEconomyService{TCurrency}"/> через конструктор.
/// </para>
/// </summary>
/// <typeparam name="TCurrency">Проектный enum с типами валюты.</typeparam>
/// <example>
/// <code>
/// public class CurrencyMapper : ICurrencyMapper&lt;CurrencyType&gt;
/// {
///     public string ToServiceId(CurrencyType c) => c.ToUGSId(); // проектный extension-метод
///     public bool IsOfflineAllowed(CurrencyType c, InventoryOperation op) => c.IsOfflineAllowed(op);
/// }
/// </code>
/// </example>
public interface ICurrencyMapper<TCurrency> where TCurrency : struct, Enum
{
    /// <summary>
    /// Конвертирует проектный enum в строковый ID для SDK.
    /// Значение ДОЛЖНО совпадать с ID ресурса в бэкенде (UGS Dashboard и т.д.).
    /// </summary>
    string ToServiceId(TCurrency currency);

    /// <summary>
    /// Определяет, разрешена ли данная операция без подключения к сети.
    /// Единая точка истины для офлайн-логики — не дублируйте правила в других местах.
    /// </summary>
    bool IsOfflineAllowed(TCurrency currency, InventoryOperation op);
}
