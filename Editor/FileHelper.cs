using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BundleSystem
{
    public class FileHelper
    {
        public static void CopyDirectory(string sourceDirName, string destDirName, StringBuilder logs = null)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo sourceDir = new DirectoryInfo(sourceDirName);

            if (!sourceDir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = sourceDir.GetDirectories();
        
            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);        

            // Get the files in the directory and copy them to the new location.
            foreach (var file in sourceDir.GetFiles())
            {
                if (file.Name.EndsWith(".meta")) continue;
                string destFilePath = Path.Combine(destDirName, file.Name);
                file.CopyTo(destFilePath, true);
                logs?.AppendLine($"Copied the file from {file} into {destFilePath}");
            }
            
            var newFiles = new HashSet<string>(sourceDir.GetFiles().Select(f=>f.Name).Distinct());
            DirectoryInfo destDir = new DirectoryInfo(destDirName);
            foreach (var file in destDir.GetFiles())
            {
                if (file.Name.EndsWith(".meta")) continue;
                if (newFiles.Contains(file.Name) == false)
                {
                    string destFilePath = Path.Combine(destDirName, file.Name); 
                    File.Delete(destFilePath);
                    logs?.AppendLine($"Deleted the file {destFilePath}");
                }
            }

            foreach (var subDir in dirs)
            {
                string tempPath = Path.Combine(destDirName, subDir.Name);
                CopyDirectory(subDir.FullName, tempPath, logs);
            }
        }
    }
}