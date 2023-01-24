using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BundleSystem
{
    [CreateAssetMenu(menuName = "Asset Bundle/Build Global Settings")]
    public class AssetBundleBuildGlobalSettings : ScriptableObject
    {
        public AssetBundleDistributionProfile editorRunningProfile;

        [Header("Disallow cross asset references between AssetBundlePackageBuildSettings")]
        public bool disallowCrossReference = true;

        [Serializable]
        public class DistributionProfileByPlatform
        {
            public PlatformType platform;
            public AssetBundleDistributionProfile distributionProfile;
        }

        public DistributionProfileByPlatform[] profiles;

        public AssetBundleDistributionProfile GetDistributionProfileByPlatform(PlatformType platform)
        {
            return profiles?.FirstOrDefault(p => p.platform == platform)?.distributionProfile;
        }
        
        #if UNITY_EDITOR
        public AssetBundleDistributionProfile GetDistributionProfile()
        {
            PlatformType? activeBuildTarget = null;
            switch (EditorUserBuildSettings.activeBuildTarget)
            {
                case BuildTarget.Android: activeBuildTarget = PlatformType.Android;
                    break;
                case BuildTarget.iOS: activeBuildTarget = PlatformType.IOS;
                    break;
            }

            if (activeBuildTarget == null) return null;
            
            return profiles?.FirstOrDefault(p => p.platform == activeBuildTarget)?.distributionProfile;
        }
        
        public AssetBundlePackageBuildSettings[] GetActiveSettingEntries()
        {
            return settings?
                       .Where(setting => setting.include && setting.setting != null)
                       .Select(setting => setting.setting)
                       .ToArray() ??
                   Array.Empty<AssetBundlePackageBuildSettings>();
        }

        public AssetBundlePackageBuildSettings[] GetActiveBuiltInBundleEntries()
        {
            return GetActiveSettingEntries()
                .Where(setting => setting.BundleSettings.Any(bundle => bundle.IncludedInPlayer))
                .ToArray();
        }
        #endif

        public SettingEntry[] settings;

    }

    [Serializable]
    public class SettingEntry
    {
        public bool include;
        public AssetBundlePackageBuildSettings setting;
    }

    public enum PlatformType
    {
        Android,
        IOS,
    }
}