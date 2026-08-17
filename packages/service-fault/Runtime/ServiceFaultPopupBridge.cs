using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dumb bridge: forwards <see cref="ServiceFaultPool"/> state to listeners.
/// No UI, no formatting, no status decisions — consumers own presentation.
/// </summary>
[DisallowMultipleComponent]
public sealed class ServiceFaultPopupBridge : MonoBehaviour
{
    public event Action OnFaultChanged;

    void OnEnable()
    {
        ServiceFaultPool.OnPoolChanged += HandlePoolChanged;
        HandlePoolChanged();
    }

    void OnDisable()
    {
        ServiceFaultPool.OnPoolChanged -= HandlePoolChanged;
    }

    public bool TryGetTopFault(out ServiceFaultEntry entry) =>
        ServiceFaultPool.TryPeekHighest(out entry);

    public IReadOnlyList<ServiceFaultEntry> ActiveFaults =>
        ServiceFaultPool.Active;

    public void NotifyDismissed(string faultId)
    {
        if (string.IsNullOrWhiteSpace(faultId))
            return;

        ServiceFaultPool.Dismiss(faultId);
    }

    public bool IsFaultActive(string faultId)
    {
        if (string.IsNullOrWhiteSpace(faultId))
            return false;

        var active = ServiceFaultPool.Active;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i] != null && active[i].Id == faultId)
                return true;
        }

        return false;
    }

    void HandlePoolChanged() => OnFaultChanged?.Invoke();
}
