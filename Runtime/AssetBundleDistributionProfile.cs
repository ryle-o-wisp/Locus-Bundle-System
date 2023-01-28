using UnityEngine;

namespace BundleSystem
{
    [CreateAssetMenu(menuName = "Asset Bundle/Distribution Profile")]
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
        public string localOutputFolder = "LocalBundles";

        [Tooltip("Remote URL for downloading remote bundles")]
        public string remoteURL = "http://localhost/";
    }
}