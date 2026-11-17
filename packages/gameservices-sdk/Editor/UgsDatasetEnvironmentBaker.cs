using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
#if UNITY_6000_0_OR_NEWER
using UnityEditor.Build.Profile;
#endif

namespace RamnD.GameServices.Editor
{
    /// <summary>
    /// Resolves <see cref="UgsEnvironmentNameMap"/> against the active Build Profile
    /// and writes the chosen dashboard name where the player can read it.
    /// Scripting defines are compile-time flags, so a data list cannot be tested with #if.
    /// The player build copies the already imported asset, so the write happens when the
    /// map is saved and again before Unity packs player content. Play Mode is not required.
    /// </summary>
    [InitializeOnLoad]
    sealed class UgsDatasetEnvironmentBaker : IPreprocessBuildWithReport
    {
        const string MultipleMapsKey = "RamnD.GameServices.UgsDatasetEnvironment.MultipleMaps";

        private static bool _baking;

        static UgsDatasetEnvironmentBaker()
        {
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            ScheduleBake();
        }

        static void OnCompilationFinished(object _)
        {
            ScheduleBake();
        }

        /// <summary>Runs <see cref="Bake"/> on the next editor tick, after the current import or domain reload.</summary>
        internal static void ScheduleBake()
        {
            EditorApplication.delayCall -= Bake;
            EditorApplication.delayCall += Bake;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Domain reload for Play Mode reads the generated file. Write it before that reload.
            if (state == PlayModeStateChange.ExitingEditMode)
                Bake();
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Bake();

        internal static void Bake()
        {
            if (_baking || EditorApplication.isPlaying)
                return;

            _baking = true;
            try
            {
                List<UgsEnvironmentNameEntry> entries = LoadEntries();
                HashSet<string> defines = CollectDefines();
                UgsEnvironmentNameMap.TrySelect(entries, defines, out string environmentName, out int matchCount);

                if (matchCount > 1)
                {
                    Debug.LogWarning(
                        "SDK: more than one UGS dataset symbol from UgsEnvironmentNameMap is defined. " +
                        $"Using '{environmentName}', the earliest row in the list.");
                }

                WarnUnmapped(entries, defines);
                WriteDatasetAsset(environmentName);
            }
            finally
            {
                _baking = false;
            }
        }

        static List<UgsEnvironmentNameEntry> LoadEntries()
        {
            string[] guids = AssetDatabase.FindAssets("t:UgsEnvironmentNameMap");
            var paths = new List<string>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            if (paths.Count > 1 && !SessionState.GetBool(MultipleMapsKey, false))
            {
                SessionState.SetBool(MultipleMapsKey, true);
                Debug.LogWarning(
                    "SDK: more than one UgsEnvironmentNameMap asset. " +
                    "Rows are read in asset-path order. Keep a single map.");
            }

            var entries = new List<UgsEnvironmentNameEntry>();
            for (int i = 0; i < paths.Count; i++)
            {
                UgsEnvironmentNameMap map = AssetDatabase.LoadAssetAtPath<UgsEnvironmentNameMap>(paths[i]);
                if (map == null || map.Entries == null)
                    continue;

                for (int row = 0; row < map.Entries.Count; row++)
                    entries.Add(map.Entries[row]);
            }

            return entries;
        }

        static HashSet<string> CollectDefines()
        {
            var defines = new HashSet<string>(StringComparer.Ordinal);
            UnityEditor.Compilation.Assembly[] assemblies = CompilationPipeline.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                string[] assemblyDefines = assemblies[i].defines;
                if (assemblyDefines == null)
                    continue;

                for (int d = 0; d < assemblyDefines.Length; d++)
                    AddDefine(defines, assemblyDefines[d]);
            }

#if UNITY_6000_0_OR_NEWER
            BuildProfile profile = BuildProfile.GetActiveBuildProfile();
            if (profile != null && profile.scriptingDefines != null)
            {
                foreach (string define in profile.scriptingDefines)
                    AddDefine(defines, define);
            }
#endif
            return defines;
        }

        static void AddDefine(HashSet<string> defines, string symbol)
        {
            if (!string.IsNullOrWhiteSpace(symbol))
                defines.Add(symbol.Trim());
        }

        static void WarnUnmapped(List<UgsEnvironmentNameEntry> entries, HashSet<string> defines)
        {
            foreach (string symbol in defines)
            {
                if (!symbol.StartsWith(UgsEnvironmentNameMap.SymbolPrefix, StringComparison.Ordinal))
                    continue;

                if (HasUsableRow(entries, symbol))
                    continue;

                string key = "RamnD.GameServices.UgsDatasetEnvironment.Unmapped." + symbol;
                if (SessionState.GetBool(key, false))
                    continue;

                SessionState.SetBool(key, true);
                Debug.LogError(
                    $"SDK: scripting define '{symbol}' is set, but UgsEnvironmentNameMap has no row " +
                    "with that symbol and a dashboard environment name. The build keeps the tier dataset.");
            }
        }

        static bool HasUsableRow(List<UgsEnvironmentNameEntry> entries, string symbol)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                UgsEnvironmentNameEntry entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.symbol) || string.IsNullOrWhiteSpace(entry.environmentName))
                    continue;

                if (string.Equals(entry.symbol.Trim(), symbol, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static void WriteDatasetAsset(string environmentName)
        {
            string next = environmentName ?? string.Empty;
            string path = UgsDatasetEnvironment.AssetPath;
            UgsDatasetEnvironment asset = AssetDatabase.LoadAssetAtPath<UgsDatasetEnvironment>(path);
            if (asset == null)
            {
                if (next.Length == 0)
                    return;

                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    directory = directory.Replace('\\', '/');
                    AssetDatabase.ImportAsset(directory);
                }

                asset = ScriptableObject.CreateInstance<UgsDatasetEnvironment>();
                AssetDatabase.CreateAsset(asset, path);
            }

            SerializedObject serialized = new SerializedObject(asset);
            SerializedProperty property = serialized.FindProperty("_environmentName");
            string current = property != null ? property.stringValue : asset.EnvironmentName;
            if (string.Equals(current, next, StringComparison.Ordinal))
                return;

            if (property != null)
            {
                property.stringValue = next;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                asset.SetEnvironmentName(next);
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            // The player build packs the imported artifact. SaveAssets updates the file;
            // a synchronous import updates the artifact before this call returns.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(next.Length == 0
                ? "SDK: UGS dataset override cleared. The tier name is used."
                : $"SDK: UGS dataset override set to '{next}'.");
        }
    }

    /// <summary>
    /// Writes the dataset asset before Unity collects player content.
    /// <see cref="IPreprocessBuildWithReport"/> runs too late: that build still packs the previous artifact.
    /// </summary>
    sealed class UgsDatasetEnvironmentBuildProcessor : BuildPlayerProcessor
    {
        /// <summary>Earlier than the default so the imported asset exists for later build processors.</summary>
        public override int callbackOrder => -1000;

        /// <summary>Writes the active profile's dataset name into <see cref="UgsDatasetEnvironment"/>.</summary>
        /// <param name="buildPlayerContext">Scheduled player build. The name is resolved from the active Build Profile.</param>
        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            _ = buildPlayerContext;
            UgsDatasetEnvironmentBaker.Bake();
        }
    }

    /// <summary>Keeps the generated dataset asset in step with the name map without entering Play Mode.</summary>
    sealed class UgsEnvironmentNameMapPostprocessor : AssetPostprocessor
    {
        /// <summary>Schedules a dataset write when the name map is imported or moved.</summary>
        /// <param name="importedAssets">Assets imported by this refresh.</param>
        /// <param name="deletedAssets">Assets deleted by this refresh.</param>
        /// <param name="movedAssets">Assets moved by this refresh.</param>
        /// <param name="movedFromAssetPaths">Former paths of <paramref name="movedAssets"/>.</param>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!ContainsNameMap(importedAssets) &&
                !ContainsNameMap(deletedAssets) &&
                !ContainsNameMap(movedAssets) &&
                !ContainsNameMap(movedFromAssetPaths))
                return;

            UgsDatasetEnvironmentBaker.ScheduleBake();
        }

        /// <summary>True when any path is a <see cref="UgsEnvironmentNameMap"/> asset.</summary>
        /// <param name="paths">Asset paths from the import callback.</param>
        private static bool ContainsNameMap(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (AssetDatabase.GetMainAssetTypeAtPath(paths[i]) == typeof(UgsEnvironmentNameMap))
                    return true;
            }

            return false;
        }
    }
}
