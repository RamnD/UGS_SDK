/// <summary>
/// Тип операции с балансом. Используется в <see cref="ICurrencyMapper{TCurrency}.IsOfflineAllowed"/>
/// для определения офлайн-правил на уровне проекта.
/// </summary>
public enum InventoryOperation
{
    /// <summary>Начисление валюты (награда, покупка в магазине реального мира).</summary>
    Add,

    /// <summary>Списание валюты (покупка внутри игры, расход жизни).</summary>
    Spend
}
