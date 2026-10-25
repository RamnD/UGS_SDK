using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace RamnD.GameServices.Editor
{
    /// <summary>
    /// Stops a reloaded <c>AnalyticsContainer</c> from touching a cleared service reference.
    /// <c>Update</c> throws every frame, and <c>OnApplicationPause</c> still runs on a
    /// disabled behaviour while its GameObject stays active. Deactivating the object
    /// covers both. The next <c>UnityServices.InitializeAsync</c> creates a fresh container.
    /// </summary>
    [InitializeOnLoad]
    static class UnityAnalyticsContainerGuard
    {
        const string ContainerTypeName = "Unity.Services.Analytics.AnalyticsContainer, Unity.Services.Analytics";
        const int RestoreSweepPasses = 2;

        static int s_RestoreSweepPassesLeft;

        static UnityAnalyticsContainerGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // Also schedule below, in case this constructor ran after the event.
            AssemblyReloadEvents.afterAssemblyReload += ScheduleCleanupAfterRestore;
            ScheduleCleanupAfterRestore();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void SilenceAfterDomainReload()
        {
            DeactivateContainersWithoutService();
        }

        /// <summary>
        /// Script recompile while Play Mode stays active does not raise
        /// <see cref="PlayModeStateChange.ExitingEditMode"/> and does not run
        /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/>. Queue the sweep
        /// again from here in case the earlier callbacks ran before object restore.
        /// </summary>
        [DidReloadScripts]
        static void OnScriptsReloaded()
        {
            ScheduleCleanupAfterRestore();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                DeactivateContainersWithoutService();
        }

        static void ScheduleCleanupAfterRestore()
        {
            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // Reload callbacks run before Unity writes serialized state back onto a
            // surviving container. The first editor tick can still be that early, so
            // sweep once per tick across two ticks. A live service is left alone.
            s_RestoreSweepPassesLeft = RestoreSweepPasses;
            EditorApplication.delayCall += DeactivateRestoredContainers;
            EditorApplication.update -= DeactivateOnEditorUpdate;
            EditorApplication.update += DeactivateOnEditorUpdate;
        }

        static void DeactivateOnEditorUpdate()
        {
            // Entering Play Mode can tick once before isPlaying is true. Those
            // ticks must not use up the post-restore sweeps.
            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    s_RestoreSweepPassesLeft = 0;
                    EditorApplication.update -= DeactivateOnEditorUpdate;
                }
                return;
            }

            try
            {
                DeactivateRestoredContainers();
            }
            finally
            {
                if (s_RestoreSweepPassesLeft > 0)
                    s_RestoreSweepPassesLeft--;
                if (s_RestoreSweepPassesLeft <= 0)
                    EditorApplication.update -= DeactivateOnEditorUpdate;
            }
        }

        static void DeactivateRestoredContainers()
        {
            if (!EditorApplication.isPlaying)
                return;

            DeactivateContainersWithoutService();
        }

        static void DeactivateContainersWithoutService()
        {
            Type containerType = Type.GetType(ContainerTypeName);
            if (containerType == null)
                return;

            FieldInfo serviceField = containerType.GetField("m_Service", BindingFlags.Instance | BindingFlags.NonPublic);
            if (serviceField == null)
                return;

            foreach (UnityEngine.Object container in Resources.FindObjectsOfTypeAll(containerType))
            {
                Behaviour behaviour = container as Behaviour;
                if (behaviour == null || EditorUtility.IsPersistent(behaviour))
                    continue;
                if (serviceField.GetValue(behaviour) != null || !behaviour.gameObject.activeSelf)
                    continue;

                behaviour.gameObject.SetActive(false);
            }
        }
    }
}
