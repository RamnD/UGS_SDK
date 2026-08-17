using System;

/// <summary>
/// One active fault in <see cref="ServiceFaultPool"/>. Immutable identity (<see cref="Id"/>);
/// mutable counters for dedupe diagnostics.
/// </summary>
public sealed class ServiceFaultEntry
{
    public string Id { get; }
    public ServiceFaultDomain Domain { get; }
    public string FaultKey { get; }
    public ServiceFaultStatus Status { get; }
    public string Title { get; }
    public string Description { get; }
    public string Code { get; }
    public int Severity { get; }

    /// <summary>
    /// Sticky faults suppress on dismiss until <see cref="ServiceFaultPool.Clear"/>.
    /// One-shot faults clear fully on dismiss.
    /// </summary>
    public bool IsSticky { get; }

    public int OccurredCount { get; private set; }
    public long LastSeenUtcTicks { get; private set; }

    public ServiceFaultEntry(
        string id,
        ServiceFaultDomain domain,
        string faultKey,
        ServiceFaultStatus status,
        string title,
        string description,
        string code,
        bool isSticky = false)
    {
        Id = id ?? string.Empty;
        Domain = domain;
        FaultKey = faultKey ?? string.Empty;
        Status = status;
        Title = title ?? string.Empty;
        Description = description ?? string.Empty;
        Code = code ?? string.Empty;
        Severity = (int)status;
        IsSticky = isSticky;
        OccurredCount = 1;
        LastSeenUtcTicks = DateTime.UtcNow.Ticks;
    }

    internal void MarkSeenAgain()
    {
        OccurredCount++;
        LastSeenUtcTicks = DateTime.UtcNow.Ticks;
    }
}
