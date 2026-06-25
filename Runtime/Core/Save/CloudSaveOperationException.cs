using System;

/// <summary>Ошибка загрузки или отправки данных в облако (исключая «нет данных»).</summary>
public sealed class CloudSaveOperationException : Exception
{
    public CloudSaveOperationException(string message, Exception innerException = null) : base(message, innerException) { }
}
