using System;
using System.Linq;
using System.Text;

namespace BundleSystem
{
    public static class UniquePackageNameValidator
    {
        private static string[] FindDuplicatedPackageName(AssetBundleBuildGlobalSettings settings)
        {
            var allNames = settings.settings
                .Where(entry => entry.setting != null && entry.include)
                .Select(entry => entry.setting.name)
                .ToArray();

            return allNames
                .GroupBy(name => name)
                .Where(group => group.Count() > 1)
                .Select(group=>group.Key)
                .ToArray();
        }

        public static void AssertIfInvalid(AssetBundleBuildGlobalSettings settings)
        {
            var duplicatedName = FindDuplicatedPackageName(settings);
            if (duplicatedName.Length > 0)
            {
                throw new DuplicatedPackageFoundException(duplicatedName, $"Found duplicated names({string.Join(",", duplicatedName)})");
            }
        }
        
        public static string CreateReport(AssetBundleBuildGlobalSettings globalSettings)
        {
            var foundPattern = FindDuplicatedPackageName(globalSettings).ToArray();
            var sb = new StringBuilder();
            sb.AppendLine("------------------------------------------------------------------------------------------");
            sb.AppendLine($"Duplicated package name {foundPattern.Length} cases");
            sb.AppendLine($"Report date: {System.DateTime.Now}");
            sb.AppendLine("------------------------------------------------------------------------------------------");
            foreach (var found in foundPattern)
            {
                sb.AppendLine(found);
            }
            return sb.ToString().Trim();
        }
    }

    public sealed class DuplicatedPackageFoundException : Exception
    {
        public DuplicatedPackageFoundException(string[] duplicatedNames, string message) : base(message)
        {
        }
    }
}