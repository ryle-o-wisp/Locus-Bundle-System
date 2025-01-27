#if UNITY_EDITOR
using UnityEditor;

namespace BundleSystem
{
    public static class BuildTargetExtension
    {
        public static PlatformType ToPlatformType(this BuildTarget self)
        {
            switch (self)
            {
                case BuildTarget.Android:
                    return PlatformType.Android;
                case BuildTarget.iOS:
                    return PlatformType.IOS;
                default: return default;
            }
        } 
    }
}
#endif