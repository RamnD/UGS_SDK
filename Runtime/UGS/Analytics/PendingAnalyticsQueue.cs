using System;
using UnityEngine;

/// <summary>
/// Disk-backed queue for analytics events while offline or before analytics init.
/// </summary>
internal sealed class PendingAnalyticsQueue
{
    const string PrefsKey = "analytics_pending_events";
    const int MaxEvents = 500;

    public int Count => Load().items?.Length ?? 0;

    public void Enqueue(PendingAnalyticsRecord record)
    {
        if (record == null || string.IsNullOrEmpty(record.eventName))
            return;

        var queue = Load();
        var items = queue.items ?? Array.Empty<PendingAnalyticsRecord>();
        var next = new PendingAnalyticsRecord[items.Length + 1];
        Array.Copy(items, next, items.Length);
        next[^1] = record;

        if (next.Length > MaxEvents)
        {
            int overflow = next.Length - MaxEvents;
            var trimmed = new PendingAnalyticsRecord[MaxEvents];
            Array.Copy(next, overflow, trimmed, 0, MaxEvents);
            next = trimmed;
        }

        queue.items = next;
        Persist(queue);
    }

    public bool TryDequeue(out PendingAnalyticsRecord record)
    {
        var queue = Load();
        var items = queue.items;
        if (items == null || items.Length == 0)
        {
            record = null;
            return false;
        }

        record = items[0];
        if (items.Length == 1)
        {
            queue.items = Array.Empty<PendingAnalyticsRecord>();
        }
        else
        {
            var next = new PendingAnalyticsRecord[items.Length - 1];
            Array.Copy(items, 1, next, 0, next.Length);
            queue.items = next;
        }

        Persist(queue);
        return true;
    }

    public void Clear()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }

    static PendingAnalyticsQueueData Load()
    {
        string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return new PendingAnalyticsQueueData();

        return JsonUtility.FromJson<PendingAnalyticsQueueData>(json) ?? new PendingAnalyticsQueueData();
    }

    static void Persist(PendingAnalyticsQueueData queue)
    {
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(queue));
        PlayerPrefs.Save();
    }
}
