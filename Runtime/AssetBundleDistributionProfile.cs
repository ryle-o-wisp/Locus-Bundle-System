using UnityEngine;

namespace BundleSystem
{
    [CreateAssetMenu(fileName = "new AssetBundleDistributionProfile", menuName = "Assets/AssetBundleDistributionProfile")]
    public class AssetBundleDistributionProfile : ScriptableObject
    {
        /// <summary>
        /// output folder inside project
        /// </summary>
        [SerializeField]
        [Tooltip("Remote bundle build output folder")]
        public string remoteOutputFolder = "RemoteBundles";
        
        /// <summary>
        /// output folder inside project
        /// </summary>
        [SerializeField]
        [Tooltip("Local bundle build output folder")]
        public string localOutputFolder = "Assets/StreamingAssets/BuiltInAssets";

        [Tooltip("Remote URL for downloading remote bundles")]
        public string remoteURL = "http://localhost/";
    }
}