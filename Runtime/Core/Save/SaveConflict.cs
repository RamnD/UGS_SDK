using System;

/// <summary>
/// Информация о конфликте между локальным и облачным сохранением.
/// Возвращается из <see cref="ICloudSaveService{TKey}.LoadAsync"/> когда обе версии существуют
/// и имеют разные временны́е метки.
/// <para>
/// Игрок должен выбрать: <see cref="ICloudSaveService{TKey}.ApplyCloud"/> или
/// <see cref="ICloudSaveService{TKey}.KeepLocal"/>.
/// </para>
/// </summary>
public readonly struct SaveConflict
{
    /// <summary>Время последнего локального изменения (UTC).</summary>
    public readonly DateTime LocalTimestamp;

    /// <summary>Время последнего облачного сохранения (UTC).</summary>
    public readonly DateTime CloudTimestamp;

    /// <summary>True если облачное сохранение новее локального.</summary>
    public bool IsCloudNewer => CloudTimestamp > LocalTimestamp;

    public SaveConflict(DateTime localTimestamp, DateTime cloudTimestamp)
    {
        LocalTimestamp = localTimestamp;
        CloudTimestamp = cloudTimestamp;
    }
}
