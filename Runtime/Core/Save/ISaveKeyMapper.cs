using System;

/// <summary>
/// Маппер ключей сохранений: преобразует проектный enum в строковый ключ облачного хранилища.
/// <para>
/// Реализуйте в проекте для каждого enum ключей сохранений.
/// Пример: <c>SaveKeyMapper : ISaveKeyMapper&lt;SaveKey&gt;</c>
/// </para>
/// </summary>
/// <typeparam name="TKey">Проектный enum с ключами сохранений.</typeparam>
public interface ISaveKeyMapper<TKey> where TKey : struct, Enum
{
    /// <summary>
    /// Конвертирует enum-ключ в строку для хранения в облаке и PlayerPrefs.
    /// Используйте snake_case: "high_score", "total_runs" и т.д.
    /// </summary>
    string ToCloudKey(TKey key);
}
