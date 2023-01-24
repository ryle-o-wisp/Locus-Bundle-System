using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Linq;
using UnityEditor.Build.Pipeline.Utilities;
using System.IO;

namespace BundleSystem
{
    [DisallowMultipleComponent]
    [CustomEditor(typeof(AssetBundlePackageBuildSettings))]
    public class AssetBundlePackageBuildSettingsInspector : Editor
    {
        SerializedProperty m_SettingsProperty;
        SerializedProperty m_AutoCreateSharedBundles;
        ReorderableList list;

        private SerializedProperty m_DownloadAtInitialTime;
        SerializedProperty m_UseCacheServer;
        SerializedProperty m_CacheServerHost;
        SerializedProperty m_CacheServerPort;

        private void OnEnable()
        {
            m_SettingsProperty = serializedObject.FindProperty("BundleSettings");
            m_AutoCreateSharedBundles = serializedObject.FindProperty("AutoCreateSharedBundles");

            m_DownloadAtInitialTime = serializedObject.FindProperty("DownloadAtInitialTime");
            m_UseCacheServer = serializedObject.FindProperty("UseCacheServer");
            m_CacheServerHost = serializedObject.FindProperty("CacheServerHost");
            m_CacheServerPort = serializedObject.FindProperty("CacheServerPort");

            var settings = target as AssetBundlePackageBuildSettings;

            list = new ReorderableList(serializedObject, m_SettingsProperty, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Bundle List");
                },

                elementHeightCallback = index =>
                {
                    var element = m_SettingsProperty.GetArrayElementAtIndex(index);
                    return EditorGUI.GetPropertyHeight(element, element.isExpanded);
                },

                drawElementCallback = (rect, index, a, h) =>
                {
                    // get outer element
                    var element = m_SettingsProperty.GetArrayElementAtIndex(index);
                    rect.xMin += 10;
                    EditorGUI.PropertyField(rect, element, new GUIContent(settings.BundleSettings[index].BundleName), element.isExpanded);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var settings = target as AssetBundlePackageBuildSettings;

            list.DoLayoutList();
            bool allowBuild = true;
            if (!settings.IsValid())
            {
                GUILayout.Label("Duplicate or Empty BundleName detected");
                allowBuild = false;
            }

            var enabledBefore = GUI.enabled;
            GUI.enabled = false;
            EditorGUILayout.TextField($"{nameof(settings.PackageGuid)}", $"{settings.PackageGuid}");
            GUI.enabled = enabledBefore;
            
            GUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(m_AutoCreateSharedBundles);
            if (allowBuild && GUILayout.Button("Get Expected Sharedbundle List"))
            {
                AssetbundleBuilder.WriteExpectedSharedBundles(settings);
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(m_DownloadAtInitialTime);
            EditorGUILayout.PropertyField(m_UseCacheServer);
            if(m_UseCacheServer.boolValue)
            {
                EditorGUILayout.PropertyField(m_CacheServerHost);
                EditorGUILayout.PropertyField(m_CacheServerPort);
            }

            EditorGUILayout.Space();
            serializedObject.ApplyModifiedProperties();
        }
    }

}
