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
    public PendingAnalyticsRecord[] items = Array.Empty<PendingAnalyticsRecord>();
}
