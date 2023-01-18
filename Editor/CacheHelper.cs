using UnityEditor;
using UnityEngine;

namespace BundleSystem
{
    public static class CacheHelper
    {
        [MenuItem("Resources/Clear Cache")]
        public static void ClearCache()
        {
            Caching.ClearCache();
            Debug.Log($@"Cache cleared");
        }
    }
}