using System;

[Serializable]
internal sealed class PendingAnalyticsRecord
{
    public string eventName;
    public PendingAnalyticsParam[] parameters = Array.Empty<PendingAnalyticsParam>();
}

[Serializable]
internal sealed class PendingAnalyticsParam
{
    public string key;
    public string valueType;
    public string value;
}

[Serializable]
internal sealed class PendingAnalyticsQueueData
{
    /// <summary>
    /// UGS environment the queue was written under. UGS uploads buffered events to whatever
    /// environment the current session initialized with, so a queue from another environment
    /// must never be replayed — that is how staging events land in production.
    /// </summary>
    public string environment;

    public PendingAnalyticsRecord[] items = Array.Empty<PendingAnalyticsRecord>();
}
