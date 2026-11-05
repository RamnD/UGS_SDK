using UnityEngine;

/// <summary>
/// Generated active dataset name. The editor writes this from
/// <see cref="UgsEnvironmentNameMap"/> before Play and before a player build.
/// Lives in Resources so the player can read it. Do not edit by hand.
/// </summary>
public sealed class UgsDatasetEnvironment : ScriptableObject
{
    public const string ResourceName = "UgsDatasetEnvironment";
    public const string AssetPath = "Assets/Resources/UgsDatasetEnvironment.asset";

    [SerializeField] string _environmentName = string.Empty;

    public string EnvironmentName => _environmentName;

    internal void SetEnvironmentName(string environmentName)
    {
        _environmentName = environmentName ?? string.Empty;
    }
}
