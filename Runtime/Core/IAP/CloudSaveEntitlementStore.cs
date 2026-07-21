using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Local-first entitlement cache backed by <see cref="ICloudSaveService{TKey}"/>.
/// Persists a set of entitlement ids under a single save key.
/// </summary>
public sealed class CloudSaveEntitlementStore<TKey> where TKey : struct, Enum
{
    [Serializable]
    sealed class EntitlementSnapshot
    {
        public string[] Ids = Array.Empty<string>();
    }

    readonly ICloudSaveService<TKey> _cloudSave;
    readonly TKey _saveKey;
    readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    bool _loaded;

    public CloudSaveEntitlementStore(ICloudSaveService<TKey> cloudSave, TKey saveKey)
    {
        _cloudSave = cloudSave ?? throw new ArgumentNullException(nameof(cloudSave));
        _saveKey = saveKey;
    }

    /// <summary>Returns true when the entitlement exists in the local cache.</summary>
    public bool Has(string entitlementId)
    {
        EnsureLoaded();
        return !string.IsNullOrWhiteSpace(entitlementId) && _ids.Contains(entitlementId);
    }

    /// <summary>Adds an entitlement id to the local cache and persists it through the cloud save interface.</summary>
    public void Grant(string entitlementId)
    {
        if (string.IsNullOrWhiteSpace(entitlementId))
            return;

        EnsureLoaded();
        if (_ids.Add(entitlementId))
            Persist();
    }

    /// <summary>Adds many entitlements, ignoring empty / duplicate ids.</summary>
    public void GrantRange(IEnumerable<string> entitlementIds)
    {
        if (entitlementIds == null)
            return;

        EnsureLoaded();
        bool changed = false;
        foreach (string id in entitlementIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                changed |= _ids.Add(id);
        }

        if (changed)
            Persist();
    }

    /// <summary>Returns a copy of all known entitlement ids.</summary>
    public string[] GetAll()
    {
        EnsureLoaded();
        return _ids.ToArray();
    }

    void EnsureLoaded()
    {
        if (_loaded)
            return;

        EntitlementSnapshot snapshot = _cloudSave.Get(_saveKey, new EntitlementSnapshot());
        if (snapshot?.Ids != null)
            GrantRangeWithoutPersist(snapshot.Ids);

        _loaded = true;
    }

    void GrantRangeWithoutPersist(IEnumerable<string> entitlementIds)
    {
        foreach (string id in entitlementIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                _ids.Add(id);
        }
    }

    void Persist()
    {
        _cloudSave.Set(_saveKey, new EntitlementSnapshot
        {
            Ids = _ids.OrderBy(id => id, StringComparer.Ordinal).ToArray()
        });
    }
}
