using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace BundleSystem
{
    [CreateAssetMenu(fileName = "AssetBundlePackageBuildSettings.asset", menuName = "Create AssetBundle Build Settings", order = 999)]
    public class AssetBundlePackageBuildSettings : ScriptableObject
    {
#if UNITY_EDITOR
        /// <summary>
        /// check setting is valid
        /// </summary>
        public bool IsValid()
        {
            return !BundleSettings.GroupBy(setting => setting.BundleName).Any(group => group.Count() > 1 ||
                string.IsNullOrEmpty(group.Key));
        }

        
        /// <summary>
        /// Check if an asset is included in one of bundles in this setting
        /// </summary>
        public bool TryGetBundleNameAndAssetPath(string editorAssetPath, out string bundleName, out string assetPath)
        {
            foreach(var setting in BundleSettings)
            {
                var bundleFolderPath = UnityEditor.AssetDatabase.GUIDToAssetPath(setting.Folder.guid);
                if (editorAssetPath.StartsWith(bundleFolderPath))
                {
                    //setting does not include subfolder and asset is in subfolder
                    if (!Utility.IsAssetCanBundled(editorAssetPath)) continue;
                    var partialPath = editorAssetPath.Remove(0, bundleFolderPath.Length + 1);

                    //partial path should not contain directory seperator if include subfoler option is false
                    if (!setting.IncludeSubfolder && partialPath.IndexOf('/') > -1) break;

                    assetPath = partialPath.Remove(partialPath.LastIndexOf('.'));
                    bundleName = setting.BundleName;
                    return true;
                }
            }

            bundleName = string.Empty;
            assetPath = string.Empty;
            return false;
        }

        public string PackageGuid => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(this));
#endif
        public const string ManifestFileName = "Manifest.json";
        public static string PackageListRuntimePath => "asset_bundle_groups";

        public static string[] ReadRuntimePackageList()
        {
            var listAsset = Resources.Load<TextAsset>(PackageListRuntimePath);
            return JsonConvert.DeserializeObject<List<string>>(listAsset.text)?.ToArray();
        }
        
        public static string LocalBundleRuntimePath => Application.streamingAssetsPath + "/localbundles/";

        public List<BundleSetting> BundleSettings = new List<BundleSetting>();

        [Tooltip("Auto create shared bundles to remove duplicated assets")]
        public bool AutoCreateSharedBundles = true;

        [Tooltip("Download all remote assets at initial patch time")]
        public bool DownloadAtInitialTime;

        //build cache server settings
        public bool UseCacheServer = false;
        public string CacheServerHost;
        public int CacheServerPort;

        //ftp settings
        public bool UseFtp = false;
        public string FtpHost;
        public string FtpUserName;
        public string FtpUserPass;
    }

    [System.Serializable]
    public class BundleSetting
    {
        [Tooltip("AssetBundle Name")]
        public string BundleName;
        [Tooltip("Should this bundle included in player?")]
        public bool IncludedInPlayer = false;
        public FolderReference Folder;
        [Tooltip("Should include subfolder?")]
        public bool IncludeSubfolder = false;
        [Tooltip("Works only for remote bundle, true for LMZA, false for LZ4")]
        public bool CompressBundle = true;
    }
}

     