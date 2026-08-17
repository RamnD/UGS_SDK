/// <summary>Supplies build/version label for crash-report metadata.</summary>
public interface IBuildInfoProvider
{
    string Format();
}

/// <summary>Default build label when no provider is registered.</summary>
public sealed class DefaultBuildInfoProvider : IBuildInfoProvider
{
    public static readonly DefaultBuildInfoProvider Instance = new();

    public string Format() => "unknown";
}
