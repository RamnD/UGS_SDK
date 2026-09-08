using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Disk-backed queue for analytics events while offline or before analytics init.
/// </summary>
internal sealed class PendingAnalyticsQueue
{
    const string PrefsKey = "analytics_pending_events";
    const int MaxEvents = 500;

    readonly object _sync = new object();

    public int Count
    {
        get
        {
            lock (_sync)
                return LoadUnlocked().items?.Length ?? 0;
        }
    }

    public void Enqueue(PendingAnalyticsRecord record)
    {
        if (record == null || string.IsNullOrEmpty(record.eventName))
            return;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            var items = queue.items ?? Array.Empty<PendingAnalyticsRecord>();
            var next = new PendingAnalyticsRecord[items.Length + 1];
            Array.Copy(items, next, items.Length);
            next[^1] = record;

            if (next.Length > MaxEvents)
            {
                int overflow = next.Length - MaxEvents;
                AppLog.Warn("Analytics", $"Pending queue full — dropping {overflow} oldest event(s).");
                var trimmed = new PendingAnalyticsRecord[MaxEvents];
                Array.Copy(next, overflow, trimmed, 0, MaxEvents);
                next = trimmed;
            }

            queue.items = next;
            PersistUnlocked(queue);
        }
    }

    /// <summary>
    /// Atomically takes up to <paramref name="maxCount"/> events from the front of the queue.
    /// </summary>
    public List<PendingAnalyticsRecord> DequeueBatch(int maxCount)
    {
        var batch = new List<PendingAnalyticsRecord>();
        if (maxCount <= 0)
            return batch;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            var items = queue.items;
            if (items == null || items.Length == 0)
                return batch;

            int take = Math.Min(maxCount, items.Length);
            for (int i = 0; i < take; i++)
                batch.Add(items[i]);

            if (take >= items.Length)
            {
                queue.items = Array.Empty<PendingAnalyticsRecord>();
            }
            else
            {
                var next = new PendingAnalyticsRecord[items.Length - take];
                Array.Copy(items, take, next, 0, next.Length);
                queue.items = next;
            }

            PersistUnlocked(queue);
        }

        return batch;
    }

    /// <summary>Re-queues records at the front (failed replay).</summary>
    public void RequeueFront(IReadOnlyList<PendingAnalyticsRecord> records)
    {
        if (records == null || records.Count == 0)
            return;

        lock (_sync)
        {
            var queue = LoadUnlocked();
            var existing = queue.items ?? Array.Empty<PendingAnalyticsRecord>();
            var next = new PendingAnalyticsRecord[records.Count + existing.Length];
            for (int i = 0; i < records.Count; i++)
                next[i] = records[i];
            Array.Copy(existing, 0, next, records.Count, existing.Length);

            if (next.Length > MaxEvents)
            {
                int overflow = next.Length - MaxEvents;
                AppLog.Warn("Analytics", $"Pending queue full after requeue — dropping {overflow} oldest event(s).");
                var trimmed = new PendingAnalyticsRecord[MaxEvents];
                Array.Copy(next, overflow, trimmed, 0, MaxEvents);
                next = trimmed;
            }

            queue.items = next;
            PersistUnlocked(queue);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }
    }

    static PendingAnalyticsQueueData LoadUnlocked()
    {
        string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return NewQueue();

        var queue = JsonUtility.FromJson<PendingAnalyticsQueueData>(json);
        if (queue == null)
            return NewQueue();

        string environment = UGSEnvironmentResolver.Current;
        if (!string.Equals(queue.environment, environment, StringComparison.Ordinal))
        {
            int dropped = queue.items?.Length ?? 0;
            if (dropped > 0)
            {
                AppLog.Warn(
                    "Analytics",
                    $"Pending queue was written for environment '{queue.environment}' but this session is " +
                    $"'{environment}' — dropping {dropped} event(s) instead of cross-uploading them.");
            }

            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            return NewQueue();
        }

        return queue;
    }

    static PendingAnalyticsQueueData NewQueue() =>
        new PendingAnalyticsQueueData { environment = UGSEnvironmentResolver.Current };

    static void PersistUnlocked(PendingAnalyticsQueueData queue)
    {
        queue.environment = UGSEnvironmentResolver.Current;
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(queue));
        PlayerPrefs.Save();
    }
}
