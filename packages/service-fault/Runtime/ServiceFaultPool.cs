using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Global fault store — no UI. Reporters call <see cref="Report"/> / <see cref="Clear"/>;
/// <see cref="ServiceFaultPopupBridge"/> forwards <see cref="OnPoolChanged"/> to UI consumers.
/// </summary>
public static class ServiceFaultPool
{
    static readonly List<ServiceFaultEntry> _entries = new();
    static readonly Dictionary<string, ServiceFaultEntry> _byId = new(StringComparer.Ordinal);
    static readonly HashSet<string> _suppressedIds = new(StringComparer.Ordinal);
    static IServiceFaultCatalog _catalog;

    /// <summary>Fired after add / clear / sort. Dedupe bumps do <b>not</b> fire (no UI spam).</summary>
    public static event Action OnPoolChanged;

    public static IReadOnlyList<ServiceFaultEntry> Active => _entries;

    public static void BindCatalog(IServiceFaultCatalog catalog) =>
        _catalog = catalog;

    public static IServiceFaultCatalog Catalog => _catalog;

    public static string BuildId(ServiceFaultDomain domain, string faultKey) =>
        $"{domain}:{faultKey ?? string.Empty}";

    public static void Report(ServiceFaultDomain domain, string faultKey, string rawCode = null)
    {
        if (string.IsNullOrWhiteSpace(faultKey))
            return;

        string id = BuildId(domain, faultKey);
        if (_suppressedIds.Contains(id))
        {
            AppLog.Verbose("Fault", $"report suppressed {id}");
            return;
        }

        if (_byId.TryGetValue(id, out ServiceFaultEntry existing))
        {
            existing.MarkSeenAgain();
            Resort();
            AppLog.Verbose("Fault", $"report bump {id} count={existing.OccurredCount}");
            return;
        }

        ResolvePresentation(domain, faultKey, rawCode,
            out ServiceFaultStatus status,
            out string title,
            out string description,
            out string code,
            out _);

        bool sticky = ServiceFaultPolicy.IsSticky(domain, faultKey);
        var entry = new ServiceFaultEntry(
            id, domain, faultKey, status, title, description, code, sticky);
        _byId[id] = entry;
        _entries.Add(entry);
        Resort();
        AppLog.Warn("Fault", $"report {id} code={code} sticky={sticky}");
        RaiseChanged();
    }

    public static bool Clear(ServiceFaultDomain domain, string faultKey)
    {
        if (string.IsNullOrWhiteSpace(faultKey))
            return false;

        string id = BuildId(domain, faultKey);
        bool hadEntryOrSuppress = _byId.ContainsKey(id) || _suppressedIds.Contains(id);
        bool removed = ClearById(id, removeSuppression: true);
        if (hadEntryOrSuppress)
            AppLog.Info("Fault", $"clear {id}");
        return removed;
    }

    public static bool ClearDomain(ServiceFaultDomain domain)
    {
        bool removed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Domain != domain)
                continue;

            string id = _entries[i].Id;
            _byId.Remove(id);
            _entries.RemoveAt(i);
            _suppressedIds.Remove(id);
            removed = true;
        }

        if (removed)
        {
            AppLog.Info("Fault", $"clear domain={domain}");
            RaiseChanged();
        }

        return removed;
    }

    public static void ClearAll()
    {
        if (_entries.Count == 0 && _suppressedIds.Count == 0)
            return;

        _entries.Clear();
        _byId.Clear();
        _suppressedIds.Clear();
        AppLog.Info("Fault", "clear all");
        RaiseChanged();
    }

    public static void ClearActiveOnReconnect()
    {
        string networkOfflineId = BuildId(ServiceFaultDomain.Network, ServiceFaultKeys.NetworkOffline);
        bool changed = _suppressedIds.Remove(networkOfflineId);

        if (_entries.Count > 0)
        {
            _entries.Clear();
            _byId.Clear();
            changed = true;
        }

        if (!changed)
            return;

        AppLog.Info("Fault", "reconnect cleanup (drop queued faults)");
        RaiseChanged();
    }

    public static void Dismiss(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        bool sticky = _byId.TryGetValue(id, out ServiceFaultEntry entry) && entry.IsSticky;
        if (sticky)
            _suppressedIds.Add(id);

        AppLog.Info("Fault", sticky ? $"dismiss sticky {id}" : $"dismiss {id}");
        ClearById(id, removeSuppression: !sticky);
    }

    public static bool TryPeekHighest(out ServiceFaultEntry entry)
    {
        if (_entries.Count == 0)
        {
            entry = null;
            return false;
        }

        entry = _entries[0];
        return true;
    }

    public static string FormatDisplayBody(ServiceFaultEntry entry)
    {
        if (entry == null)
            return string.Empty;

        string description = entry.Description ?? string.Empty;
        if (string.IsNullOrWhiteSpace(entry.Code))
            return description;

        if (string.IsNullOrWhiteSpace(description))
            return entry.Code;

        return $"{description}\n{entry.Code}";
    }

    public static bool TryTakeDomainPresentation(
        ServiceFaultDomain domain,
        out string title,
        out string body,
        string fallbackFaultKey = null)
    {
        string faultKey = fallbackFaultKey ?? ResolveDefaultFaultKey(domain);
        title = ServiceFaultFallbacks.Title(domain, faultKey);
        body = ServiceFaultFallbacks.Description(domain, faultKey);

        bool found = false;
        if (TryPeekHighest(out ServiceFaultEntry entry)
            && entry != null
            && entry.Domain == domain)
        {
            title = string.IsNullOrWhiteSpace(entry.Title) ? title : entry.Title;
            body = FormatDisplayBody(entry);
            found = true;
        }

        ClearDomain(domain);
        return found;
    }

    static string ResolveDefaultFaultKey(ServiceFaultDomain domain) =>
        domain switch
        {
            ServiceFaultDomain.Ads => ServiceFaultKeys.AdsUnavailable,
            ServiceFaultDomain.Purchases => ServiceFaultKeys.PurchasesFailed,
            ServiceFaultDomain.Auth => ServiceFaultKeys.AuthFailed,
            ServiceFaultDomain.Network => ServiceFaultKeys.NetworkOffline,
            ServiceFaultDomain.Economy => ServiceFaultKeys.EconomyFailed,
            ServiceFaultDomain.CloudSave => ServiceFaultKeys.CloudSaveFailed,
            _ => "failed",
        };

    public static Sprite ResolveIcon(ServiceFaultEntry entry)
    {
        if (entry == null)
            return null;

        if (_catalog != null
            && _catalog.TryResolve(
                entry.Domain,
                entry.FaultKey,
                entry.Code,
                out _,
                out _,
                out _,
                out _,
                out Sprite icon)
            && icon != null)
        {
            return icon;
        }

        return _catalog != null ? _catalog.GetStatusSprite(entry.Status) : null;
    }

    static bool ClearById(string id, bool removeSuppression)
    {
        if (!_byId.Remove(id))
        {
            if (removeSuppression)
                _suppressedIds.Remove(id);
            return false;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Id != id)
                continue;

            _entries.RemoveAt(i);
            break;
        }

        if (removeSuppression)
            _suppressedIds.Remove(id);

        RaiseChanged();
        return true;
    }

    static void ResolvePresentation(
        ServiceFaultDomain domain,
        string faultKey,
        string rawCode,
        out ServiceFaultStatus status,
        out string title,
        out string description,
        out string code,
        out Sprite icon)
    {
        if (_catalog != null
            && _catalog.TryResolve(domain, faultKey, rawCode, out status, out title, out description, out code, out icon))
        {
            if (string.IsNullOrWhiteSpace(code))
                code = ServiceFaultFallbacks.Code(domain, faultKey, rawCode);
            return;
        }

        status = ServiceFaultStatus.Error;
        title = ServiceFaultFallbacks.Title(domain, faultKey);
        description = ServiceFaultFallbacks.Description(domain, faultKey);
        code = ServiceFaultFallbacks.Code(domain, faultKey, rawCode);
        icon = null;
    }

    static void Resort()
    {
        _entries.Sort(static (a, b) =>
        {
            int bySeverity = b.Severity.CompareTo(a.Severity);
            if (bySeverity != 0)
                return bySeverity;
            return b.LastSeenUtcTicks.CompareTo(a.LastSeenUtcTicks);
        });
    }

    static void RaiseChanged()
    {
        AppDiagnostics.RefreshFaultSnapshot();
        OnPoolChanged?.Invoke();
    }

    public static (string topFaultId, string faultDomainsCsv)? BuildDiagnosticsSnapshot()
    {
        if (!TryPeekHighest(out ServiceFaultEntry top) || top == null)
            return null;

        var domains = new HashSet<ServiceFaultDomain>();
        var sb = new StringBuilder(32);
        for (int i = 0; i < _entries.Count; i++)
        {
            ServiceFaultEntry entry = _entries[i];
            if (entry == null || !domains.Add(entry.Domain))
                continue;

            if (sb.Length > 0)
                sb.Append(',');
            sb.Append(entry.Domain);
        }

        return (top.Id ?? string.Empty, sb.ToString());
    }
}
