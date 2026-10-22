using System;
using System.Reflection;
using UnityEditor;
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

        static UnityAnalyticsContainerGuard()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void SilenceAfterDomainReload()
        {
            DeactivateContainersWithoutService();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
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
                if (container is not Behaviour behaviour || EditorUtility.IsPersistent(behaviour))
                    continue;
                if (serviceField.GetValue(behaviour) != null || !behaviour.gameObject.activeSelf)
                    continue;

                behaviour.gameObject.SetActive(false);
            }
        }
    }
}
