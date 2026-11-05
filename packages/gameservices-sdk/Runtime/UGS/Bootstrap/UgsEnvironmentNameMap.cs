using System;
using System.Collections.Generic;

/// <summary>
/// Project config that maps Build Profile scripting defines to UGS dashboard
/// environment names. Add a row here instead of a constant in the SDK.
/// The first row whose <see cref="UgsEnvironmentNameEntry.symbol"/> is defined wins.
/// </summary>
[UnityEngine.CreateAssetMenu(fileName = "UgsEnvironmentNames", menuName = "RamnD/UGS Environment Names")]
public sealed class UgsEnvironmentNameMap : UnityEngine.ScriptableObject
{
    public const string SymbolPrefix = "UGS_ENV_NAME_";

    [UnityEngine.Tooltip(
        "Custom dashboard environments. Put the symbol on the Build Profile next to the tier symbol. " +
        "The first row whose symbol is defined is the dataset name.")]
    [UnityEngine.SerializeField] UgsEnvironmentNameEntry[] _entries = Array.Empty<UgsEnvironmentNameEntry>();

    public IReadOnlyList<UgsEnvironmentNameEntry> Entries =>
        _entries ?? Array.Empty<UgsEnvironmentNameEntry>();

    /// <summary>
    /// Picks the dashboard name for the defines that are actually set.
    /// Blank rows are ignored. <paramref name="matchCount"/> counts valid rows whose symbol is defined.
    /// </summary>
    public static bool TrySelect(
        IReadOnlyList<UgsEnvironmentNameEntry> entries,
        IEnumerable<string> definedSymbols,
        out string environmentName,
        out int matchCount)
    {
        environmentName = null;
        matchCount = 0;
        if (entries == null || definedSymbols == null)
            return false;

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (string symbol in definedSymbols)
        {
            if (!string.IsNullOrWhiteSpace(symbol))
                defined.Add(symbol.Trim());
        }

        for (int i = 0; i < entries.Count; i++)
        {
            UgsEnvironmentNameEntry entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry.symbol) || string.IsNullOrWhiteSpace(entry.environmentName))
                continue;

            if (!defined.Contains(entry.symbol.Trim()))
                continue;

            matchCount++;
            if (environmentName == null)
                environmentName = entry.environmentName.Trim();
        }

        return environmentName != null;
    }
}

/// <summary>One custom UGS environment: a Build Profile symbol and the dashboard name it selects.</summary>
[Serializable]
public struct UgsEnvironmentNameEntry
{
    [UnityEngine.Tooltip("Scripting define on the Build Profile.")]
    public string symbol;

    [UnityEngine.Tooltip("Environment name from the Unity Dashboard. Must match exactly.")]
    public string environmentName;
}
