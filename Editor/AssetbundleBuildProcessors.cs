using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace BundleSystem
{
    public class AssetbundleBuildProcessors : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 999;

        public void OnPreprocessBuild(BuildReport report)
        {
            //no instance found
            if (Directory.Exists(AssetBundlePackageBuildSettings.LocalBundleRuntimePath)) Directory.Delete(AssetBundlePackageBuildSettings.LocalBundleRuntimePath, true);
            if (!Directory.Exists(Application.streamingAssetsPath)) Directory.CreateDirectory(Application.streamingAssetsPath);

            var globalSettings = AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings;
            if (globalSettings == null) return;
            
            if(File.Exists(AssetBundlePackageBuildSettings.PackageListRuntimePath)) File.Delete(AssetBundlePackageBuildSettings.PackageListRuntimePath);
            
            foreach (var settings in globalSettings.GetActiveSettingEntries())
            {
                var localBundleSourcePath = Utility.CombinePath(Utility.GetLocalOutputPath(settings, globalSettings.GetDistributionProfile()), EditorUserBuildSettings.activeBuildTarget.ToString());
                if(!Directory.Exists(localBundleSourcePath))
                {
                    if(Application.isBatchMode)
                    {
                        Debug.LogError("Missing built local bundle directory, Locus bundle system won't work properly.");
                        return; //we can't build now as it's in batchmode
                    }
                    else
                    {
                        var buildNow = EditorUtility.DisplayDialog("LocusBundleSystem", "Warning - Missing built local bundle directory, would you like to build now?", "Yes", "Not now");
                        if(!buildNow) return; //user declined
                        AssetbundleBuilder.BuildAssetBundles(globalSettings, BuildType.Local, EditorUserBuildSettings.activeBuildTarget.ToPlatformType());
                    }
                }

                var localBundleOutputPath =
                    Utility.CombinePath(Utility.GetLocalOutputPath(settings, globalSettings.GetDistributionProfile()),
                        EditorUserBuildSettings.activeBuildTarget.ToString());

                var localBundleRuntimePath =
                    Utility.CombinePath(AssetBundlePackageBuildSettings.LocalBundleRuntimePath, settings.PackageGuid);

                StringBuilder sb = new StringBuilder();
                FileHelper.CopyDirectory(localBundleOutputPath, localBundleRuntimePath, sb);
                Debug.Log(sb.ToString());
            }
            
            // Update asset bundle settings
            var packageGuids = globalSettings.GetActiveSettingEntries().Select(entry => entry.PackageGuid).ToList();
            var packageGuidsJson = JsonConvert.SerializeObject(packageGuids);

            if (Directory.Exists("Assets/Resources") == false) Directory.CreateDirectory("Assets/Resources");
            File.WriteAllText($"Assets/Resources/{AssetBundlePackageBuildSettings.PackageListRuntimePath}.txt", packageGuidsJson);
            AssetDatabase.Refresh();
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if(FileUtil.DeleteFileOrDirectory(AssetBundlePackageBuildSettings.LocalBundleRuntimePath))
            {
                AssetDatabase.Refresh();
            }
        }
    }
}
