using System;
using UnityEngine;

/// <summary>
/// Optional plain-string catalog for games without Unity Localization.
/// Missing keys fall back to <see cref="ServiceFaultFallbacks"/>.
/// </summary>
[CreateAssetMenu(fileName = "ServiceFaultCatalog", menuName = "RamnD/Service Fault Catalog")]
public sealed class DefaultServiceFaultCatalog : ScriptableObject, IServiceFaultCatalog
{
    [Serializable]
    public struct Entry
    {
        public ServiceFaultDomain Domain;
        public string FaultKey;
        public ServiceFaultStatus Status;
        public string Title;
        public string Description;
        [Tooltip("Optional fixed code shown in UI (e.g. NET_OFFLINE). Raw reporter code overrides when non-empty.")]
        public string CodeHint;
        [Tooltip("Optional per-entry icon; otherwise status sprite from catalog is used.")]
        public Sprite IconOverride;
    }

    [SerializeField] Entry[] _entries = Array.Empty<Entry>();

    [Header("Status sprites (UI binds by ServiceFaultStatus)")]
    [SerializeField] Sprite _infoSprite;
    [SerializeField] Sprite _successSprite;
    [SerializeField] Sprite _warningSprite;
    [SerializeField] Sprite _errorSprite;

    public ReadOnlySpan<Entry> Entries => _entries;

    public bool TryResolve(
        ServiceFaultDomain domain,
        string faultKey,
        string rawCode,
        out ServiceFaultStatus status,
        out string title,
        out string description,
        out string code,
        out Sprite icon)
    {
        status = ServiceFaultStatus.Error;
        title = null;
        description = null;
        code = rawCode;
        icon = null;

        if (string.IsNullOrWhiteSpace(faultKey))
            return false;

        for (int i = 0; i < _entries.Length; i++)
        {
            Entry entry = _entries[i];
            if (entry.Domain != domain)
                continue;
            if (!string.Equals(entry.FaultKey, faultKey, StringComparison.Ordinal))
                continue;

            status = entry.Status;
            title = string.IsNullOrWhiteSpace(entry.Title)
                ? ServiceFaultFallbacks.Title(domain, faultKey)
                : entry.Title;
            description = string.IsNullOrWhiteSpace(entry.Description)
                ? ServiceFaultFallbacks.Description(domain, faultKey)
                : entry.Description;

            if (string.IsNullOrWhiteSpace(code))
                code = entry.CodeHint;

            icon = entry.IconOverride != null
                ? entry.IconOverride
                : GetStatusSprite(status);
            return true;
        }

        return false;
    }

    public Sprite GetStatusSprite(ServiceFaultStatus status) =>
        status switch
        {
            ServiceFaultStatus.Info => _infoSprite,
            ServiceFaultStatus.Success => _successSprite,
            ServiceFaultStatus.Warning => _warningSprite,
            _ => _errorSprite,
        };
}
