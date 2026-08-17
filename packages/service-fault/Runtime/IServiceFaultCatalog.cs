using System;
using UnityEngine;

/// <summary>
/// Resolves presentation copy for a fault. Implement in game code
/// (localized catalog) or use <see cref="DefaultServiceFaultCatalog"/>.
/// </summary>
public interface IServiceFaultCatalog
{
    bool TryResolve(
        ServiceFaultDomain domain,
        string faultKey,
        string rawCode,
        out ServiceFaultStatus status,
        out string title,
        out string description,
        out string code,
        out Sprite icon);

    Sprite GetStatusSprite(ServiceFaultStatus status);
}
