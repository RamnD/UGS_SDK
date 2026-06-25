using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Сервис облачных сохранений с локальным кэшем. TKey — проектный enum ключей (например, SaveKey).
/// <para>
/// <b>Стратегия синхронизации:</b>
/// <list type="bullet">
/// <item><see cref="Set{TValue}"/> — пишет только в локальный кэш (память + PlayerPrefs).</item>
/// <item><see cref="PushToCloudAsync"/> — отправляет локальный кэш в облако. Вызывается
///   при сворачивании/закрытии приложения.</item>
/// <item><see cref="LoadAsync"/> — загружает облачные данные при старте. Если временны́е
///   метки расходятся — возвращает <see cref="SaveConflict"/> для разрешения пользователем.</item>
/// </list>
/// </para>
/// <para>
/// Сетевые ошибки приводят к <see cref="CloudSaveOperationException"/> (игра может показать retry).
/// </para>
/// </summary>
/// <typeparam name="TKey">Проектный enum ключей сохранений.</typeparam>
public interface ICloudSaveService<TKey> where TKey : struct, Enum
{
    // ── Локальный доступ ──────────────────────────────────────────────────────

    /// <summary>Дата последнего локального изменения (UTC). Null если данных ещё нет.</summary>
    DateTime? LocalTimestamp { get; }

    /// <summary>
    /// Читает значение из локального кэша. Безопасно вызывать синхронно из UI/Update.
    /// </summary>
    /// <typeparam name="TValue">Тип значения (int, long, bool, string или сериализуемый класс).</typeparam>
    /// <param name="key">Ключ сохранения.</param>
    /// <param name="defaultValue">Возвращается если ключ не найден.</param>
    TValue Get<TValue>(TKey key, TValue defaultValue = default);

    /// <summary>
    /// Записывает значение в локальный кэш и PlayerPrefs. В облако не отправляет.
    /// Для отправки вызовите <see cref="PushToCloudAsync"/>.
    /// </summary>
    void Set<TValue>(TKey key, TValue value);

    // ── Облачная синхронизация ────────────────────────────────────────────────

    /// <summary>
    /// Загружает данные из облака.
    /// <list type="bullet">
    /// <item>Нет облачных данных → возвращает null, локальные данные без изменений.</item>
    /// <item>Нет локальных данных → применяет облако, возвращает null.</item>
    /// <item>Обе версии существуют с разными метками → возвращает <see cref="SaveConflict"/>.</item>
    /// </list>
    /// При конфликте данные не изменяются до явного вызова
    /// <see cref="ApplyCloud"/> или <see cref="KeepLocal"/>.
    /// </summary>
    Task<SaveConflict?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Отправляет локальный кэш в облако с текущей временно́й меткой.
    /// Вызывать при OnApplicationPause/OnApplicationQuit.
    /// </summary>
    Task PushToCloudAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Применяет загруженный облачный снимок как локальные данные.
    /// Вызывать после того как игрок выбрал "использовать облачное сохранение".
    /// </summary>
    void ApplyCloud();

    /// <summary>
    /// Оставляет локальные данные без изменений, отбрасывает облачный снимок.
    /// Вызывать после того как игрок выбрал "использовать локальное сохранение".
    /// </summary>
    void KeepLocal();
}
