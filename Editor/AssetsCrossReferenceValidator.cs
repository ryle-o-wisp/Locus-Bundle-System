using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

namespace BundleSystem
{
    public static class AssetsCrossReferenceValidator
    {
        private static IEnumerable<(string assetPath, string[] relatedSettings)> FindCrossReferencedAssets(AssetBundlePackageBuildSettings[] allSettings)
        {
            Dictionary<string, List<string>> usedMap = new Dictionary<string, List<string>>();
            foreach (var settings in allSettings)
            {
                var bundles = AssetbundleBuilder.GetAssetBundlesList(settings);
                var treeResult = AssetDependencyTree.ProcessDependencyTree(bundles);

                foreach (var asset in treeResult.Assets)
                {
                    if (usedMap.TryGetValue(asset, out List<string> l) == false)
                    {
                        usedMap[asset] = l = new List<string>();
                    }
                    l.Add(settings.PackageGuid);
                }
            }

            foreach (var hitEntry in usedMap.Where(entry => entry.Value.Count > 1))
            {
                yield return 
                (
                    assetPath: hitEntry.Key,
                    relatedSettings: hitEntry.Value.Select(AssetDatabase.GUIDToAssetPath).ToArray()
                );
            }
        }
        
        public static void AssertIfInvalid(AssetBundleBuildGlobalSettings settings)
        {
            var allSettings = settings.GetActiveSettingEntries();
            var foundPattern = FindCrossReferencedAssets(allSettings).ToArray();
            if (foundPattern.Length > 0)
            {
                throw new AssetsCrossReferencedException(foundPattern.Select(p => p.assetPath).ToArray(),
                    $"Found {foundPattern.Length} of cross referenced patterns");
            }
        }
        
        public static string CreateReport(AssetBundleBuildGlobalSettings globalSettings)
        {
            var foundPattern = FindCrossReferencedAssets(globalSettings.GetActiveSettingEntries()).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("------------------------------------------------------------------------------------------");
            sb.AppendLine($"Found cross referenced {foundPattern.Length} cases");
            sb.AppendLine($"Report date: {System.DateTime.Now}");
            sb.AppendLine("------------------------------------------------------------------------------------------");
            foreach (var found in foundPattern)
            {
                sb.AppendLine(found.assetPath);
                sb.AppendLine($"Referenced from");
                foreach (var relatedWith in found.relatedSettings)
                {
                    sb.AppendLine($"\t{relatedWith}");
                }
                sb.AppendLine("");
            }
            return sb.ToString().Trim();
        }
    }
    
    public class AssetsCrossReferencedException : System.Exception
    {
        public readonly string[] Targets;

        public AssetsCrossReferencedException(string[] targets, string message) : base(message)
        {
            Targets = targets;
        }
    }
}