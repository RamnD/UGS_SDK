/// <summary>Severity / visual tone for a service fault popup.</summary>
public enum ServiceFaultStatus
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>Which backend / subsystem produced the fault.</summary>
public enum ServiceFaultDomain
{
    Ads = 0,
    Economy = 1,
    CloudSave = 2,
    Auth = 3,
    Purchases = 4,
    Network = 5,
}
