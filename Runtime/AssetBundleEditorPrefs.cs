#if UNITY_EDITOR
using UnityEditor;

namespace BundleSystem
{
    public static class AssetBundleEditorPrefs
    {
        private const string AssetBundleBuildGlobalSettingsPrefsKey = "__assetBundleBuildGlobalSettingsGuid";
        public static AssetBundleBuildGlobalSettings AssetBundleBuildGlobalSettings
        {
            get
            {
                var guid = EditorPrefs.GetString(AssetBundleBuildGlobalSettingsPrefsKey, "");
                if (string.IsNullOrWhiteSpace(guid)) return null;
            
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return null;

                return AssetDatabase.LoadAssetAtPath<AssetBundleBuildGlobalSettings>(path);
            }
            set
            {
                if (value == null)
                {
                    EditorPrefs.SetString(AssetBundleBuildGlobalSettingsPrefsKey, "");
                    return;
                }

                EditorPrefs.SetString(AssetBundleBuildGlobalSettingsPrefsKey,
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out var guid, out long localId) ? guid : "");
            }
        }

        private const string EmulateInEditorPrefsKey = "__assetBundleEmulateInEditor";
        public static bool EmulateInEditor
        {
            get => EditorPrefs.GetBool(EmulateInEditorPrefsKey, false);
            set => EditorPrefs.SetBool(EmulateInEditorPrefsKey, value);
        }
    
        private const string EmulateWithoutRemoteURLPrefsKey = "__assetBundleEmulateWithoutRemoteURLPrefsKey";
        public static bool EmulateWithoutRemoteURL
        {
            get => EditorPrefs.GetBool(EmulateWithoutRemoteURLPrefsKey, false);
            set => EditorPrefs.SetBool(EmulateWithoutRemoteURLPrefsKey, value);
        }
        
        
        private const string CleanCacheInEditorPrefsKey = "__assetBundleCleanCacheInEditorPrefsKey";
        public static bool CleanCacheInEditor
        {
            get => EditorPrefs.GetBool(CleanCacheInEditorPrefsKey, false);
            set => EditorPrefs.SetBool(CleanCacheInEditorPrefsKey, value);
        }
        
        
        
        private const string IncrementalBuildPrefsKey = "__assetBunldeIncrementalBuild";
        public static bool IncrementalBuild
        {
            get => EditorPrefs.GetBool(IncrementalBuildPrefsKey, false);
            set => EditorPrefs.SetBool(IncrementalBuildPrefsKey, value);
        }


    } 
}
#endif