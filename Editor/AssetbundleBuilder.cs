using System.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.WriteTypes;
using UnityEngine;
using System;
using System.Text.RegularExpressions;
using Object = UnityEngine.Object;

namespace BundleSystem
{
    public enum BuildType
    {
        Remote,
        Local
    }

    /// <summary>
    /// class that contains actual build functionalities
    /// </summary>
    public static class AssetbundleBuilder
    {
        const string LogFileName = "BundleBuildLog.txt";
        const string LogExpectedSharedBundleFileName = "ExpectedSharedBundles.txt";

        class CustomBuildParameters : BundleBuildParameters
        {
            public AssetBundlePackageBuildSettings CurrentSettings;
            public BuildType CurrentBuildType;
            public Dictionary<string, HashSet<string>> DependencyDic;

            public CustomBuildParameters(AssetBundlePackageBuildSettings settings, 
                BuildTarget target, 
                BuildTargetGroup group, 
                string outputFolder,
                Dictionary<string, HashSet<string>> deps,
                BuildType  buildType) : base(target, group, outputFolder)
            {
                CurrentSettings = settings;
                CurrentBuildType = buildType;
                DependencyDic = deps;
            }

            // Override the GetCompressionForIdentifier method with new logic
            public override BuildCompression GetCompressionForIdentifier(string identifier)
            {
                //local bundles are always lz4 for faster initializing
                if (CurrentBuildType == BuildType.Local) return BuildCompression.LZ4;

                //find user set compression method
                var found = CurrentSettings.BundleSettings.FirstOrDefault(setting => setting.BundleName == identifier);
                return found == null || !found.CompressBundle ? BuildCompression.LZ4 : BuildCompression.LZMA;
            }
        }

        public static void BuildAssetBundles(AssetBundleBuildGlobalSettings globalSettings, PlatformType? targetPlatform = null)
        {
            if (globalSettings == null) throw new ArgumentNullException(nameof(globalSettings));
            var globalSettingPath = AssetDatabase.GetAssetPath(globalSettings);
            if (string.IsNullOrEmpty(globalSettingPath))
            {
                throw new ArgumentException(
                    $"{globalSettings.name}({nameof(AssetBundleBuildGlobalSettings)}) is not a project asset");
            }

            AssetBundleBuildGlobalSettings GetGlobalSettings()
            {
                return AssetDatabase.LoadAssetAtPath<AssetBundleBuildGlobalSettings>(globalSettingPath);
            }
            
            if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings == null)
            {
                throw new InvalidProgramException($"{nameof(AssetBundleBuildGlobalSettings)} not specified");
            }

            var dependencyTree = AssetDependencyTree.ProcessDependencyTree(globalSettings.GetActiveSettingEntries());

            if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.disallowCrossReference)
            {
                AssetsCrossReferenceValidator.AssertIfInvalid(globalSettings, dependencyTree);
            }
            
            UniquePackageNameValidator.AssertIfInvalid(globalSettings);

            var distribution = globalSettings.profiles.FirstOrDefault(prof => prof.platform == targetPlatform);

            if (distribution == null)
            {
                throw new InvalidProgramException(
                    $"{globalSettings.name} does not contains distribution profile for {targetPlatform}");
            }

            globalSettings = GetGlobalSettings();

            foreach (var settings in globalSettings.GetActiveSettingEntries())
            {
                var settingPath = AssetDatabase.GetAssetPath(settings);
                if (string.IsNullOrEmpty(settingPath))
                    throw new ArgumentException($"{settings.name} in {globalSettings.name} is not a project asset");
            }

            var activeSettings = globalSettings.GetActiveSettingEntries();
            foreach (var settings in activeSettings)
            {
                var settingPath = AssetDatabase.GetAssetPath(settings);
                {
                    var setSetting = AssetDatabase.LoadAssetAtPath<AssetBundlePackageBuildSettings>(settingPath);
                    var inDistributionProfile = GetGlobalSettings().profiles.FirstOrDefault(prof => prof.platform == targetPlatform);
                    BuildAssetBundles(setSetting, inDistributionProfile?.distributionProfile, BuildType.Local, false, targetPlatform, dependency: dependencyTree);
                }
                {
                    var setSetting = AssetDatabase.LoadAssetAtPath<AssetBundlePackageBuildSettings>(settingPath);
                    var inDistributionProfile = GetGlobalSettings().profiles.FirstOrDefault(prof => prof.platform == targetPlatform);
                    BuildAssetBundles(setSetting, inDistributionProfile?.distributionProfile, BuildType.Remote, false, targetPlatform, dependency: dependencyTree);
                }
            }
        }
        
        
        public static void BuildAssetBundles(AssetBundleBuildGlobalSettings globalSettings, BuildType buildType, PlatformType? targetPlatform = null)
        {
            if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings == null)
            {
                throw new InvalidProgramException($"{nameof(AssetBundleBuildGlobalSettings)} not specified");
            }

            if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.disallowCrossReference)
            {
                AssetsCrossReferenceValidator.AssertIfInvalid(globalSettings);
            }

            UniquePackageNameValidator.AssertIfInvalid(globalSettings);

            var distribution = globalSettings.profiles.FirstOrDefault(prof => prof.platform == targetPlatform);

            if (distribution == null)
            {
                throw new InvalidProgramException(
                    $"{globalSettings.name} does not contains distribution profile for {targetPlatform}");
            }
            
            foreach (var settings in globalSettings.GetActiveSettingEntries())
            {
                BuildAssetBundles(settings, distribution.distributionProfile, buildType, false, targetPlatform);
            }
        }

        public static void WriteExpectedSharedBundles(AssetBundlePackageBuildSettings settings, string logPath=null)
        {
            WriteExpectedSharedBundles(new [] { settings }, logPath);
        }
        
        public static void WriteExpectedSharedBundles(AssetBundlePackageBuildSettings[] settings, string logPath=null)
        {
            if(!Application.isBatchMode)
            {
                //have to ask save current scene
                var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

                if(!saved) 
                {
                    EditorUtility.DisplayDialog("Failed!", $"User Canceled", "Confirm");
                    return;
                }
            }
            
            var tempPrevSceneKey = "WriteExpectedSharedBundlesPrevScene";
            var prevScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            EditorPrefs.SetString(tempPrevSceneKey, prevScene.path);

            var logFilePath = GetExpectedSharedBundleFilesLogPath(logPath ?? $"{Application.dataPath}/../");
            if(File.Exists(logFilePath)) File.Delete(logFilePath);
            
            foreach (var setting in settings)
            {
                var bundleList = GetAssetBundlesList(setting);
                var treeResult = AssetDependencyTree.ProcessDependencyTree(bundleList);
                WriteSharedBundleLog(logPath ?? $"{Application.dataPath}/../", treeResult);
            }
            
            if(!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Succeeded!", $"Check {LogExpectedSharedBundleFileName} in your project root directory!", "Confirm");
                EditorUtility.RevealInFinder(logFilePath);
            }

            //domain reloaded, we need to restore previous scene path
            var prevScenePath = EditorPrefs.GetString(tempPrevSceneKey, string.Empty);
            //back to previous scene as all processed scene's prefabs are unpacked.
            if(string.IsNullOrEmpty(prevScenePath))
            {
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
            }
            else
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(prevScenePath);
            }
        }

        public static string GetSeparatedBundleAssetName(string bundleName, string assetName, string assetGuid)
        {
            return $"{Path.GetFileNameWithoutExtension(bundleName)}.{assetName}_{assetGuid}.bundle";
        }

        public static List<AssetBundleBuild> GetAssetBundlesList(AssetBundlePackageBuildSettings settings)
        {
            var bundleList = new List<AssetBundleBuild>();

            var allFolderPaths = settings.BundleSettings.Select(setting =>
            {
                //find folder
                var folderPath = AssetDatabase.GUIDToAssetPath(setting.Folder.guid);
                if (!AssetDatabase.IsValidFolder(folderPath)) return null;
                return folderPath;
            }).Where(path => false == string.IsNullOrEmpty(path)).ToArray();

            foreach (var setting in settings.BundleSettings)
            {
                //find folder
                var folderPath = AssetDatabase.GUIDToAssetPath(setting.Folder.guid);
                if (!AssetDatabase.IsValidFolder(folderPath)) throw new Exception($"Could not found Path {folderPath} for {setting.BundleName}");

                var subTerritory = allFolderPaths.Where(path => path.StartsWith(folderPath)).ToArray();

                //collect assets
                if (setting.SeparateBundlesFileByFile)
                {
                    var assetPaths = new List<string>();
                    var loadPaths = new List<string>();
                    Utility.GetFilesInDirectory(string.Empty, assetPaths, loadPaths, folderPath, setting.IncludeSubfolder, subTerritory, Utility.BuildFileType.ASSETS);

                    for (int i = 0, l = assetPaths.Count; i < l; i++)
                    {
                        var assetFilePath = assetPaths[i];
                        var catalog = BundlePathCatalog.BuildOrUpdateFromFile(assetFilePath);
                        var catalogPath = AssetDatabase.GetAssetPath(catalog);

                        var separatedAssetPaths = new[] {catalogPath, assetFilePath};
                        var separatedAddressableNames = new[] {nameof(BundlePathCatalog), loadPaths[i]};
                        var assetGuid = AssetDatabase.AssetPathToGUID(separatedAssetPaths[1]);
                        var assetName = Path.GetFileNameWithoutExtension(separatedAssetPaths[1]);
                        var bundleName = GetSeparatedBundleAssetName(setting.BundleName, assetName, assetGuid);
                                
                        var newSeparatedBundle = new AssetBundleBuild();
                        newSeparatedBundle.assetBundleName = bundleName;
                        newSeparatedBundle.assetNames = separatedAssetPaths;
                        newSeparatedBundle.addressableNames = separatedAddressableNames;
                        bundleList.Add(newSeparatedBundle);
                    }
                }
                else
                {
                    var catalog = BundlePathCatalog.BuildOrUpdateFromFolder(folderPath);
                    var catalogPath = AssetDatabase.GetAssetPath(catalog);
                    var assetPaths = new List<string>();
                    var loadPaths = new List<string>();
                    Utility.GetFilesInDirectory(string.Empty, assetPaths, loadPaths, folderPath, setting.IncludeSubfolder, subTerritory, Utility.BuildFileType.ASSETS);

                    assetPaths.Insert(0, catalogPath);
                    loadPaths.Insert(0, nameof(BundlePathCatalog) );

                    if (assetPaths.Count > 1) // BundlePathCatalog always exists.
                    {
                        var newBundle = new AssetBundleBuild();
                        newBundle.assetBundleName = Utility.GetBundleNameWithExtension(setting.BundleName);
                        newBundle.assetNames = assetPaths.ToArray();
                        newBundle.addressableNames = loadPaths.ToArray();
                        bundleList.Add(newBundle);
                    }
                }

                {
                    var scenePaths = new List<string>();
                    var loadScenePaths = new List<string>();
                    Utility.GetFilesInDirectory(string.Empty, scenePaths, loadScenePaths, folderPath, setting.IncludeSubfolder, subTerritory, Utility.BuildFileType.SCENES);

                    if (scenePaths.Count > 0)
                    {
                        var newBundle = new AssetBundleBuild();
                        newBundle.assetBundleName = $"{Path.GetFileNameWithoutExtension(setting.BundleName)}.scenes.bundle";
                        newBundle.assetNames = scenePaths.ToArray();
                        newBundle.addressableNames = loadScenePaths.ToArray();
                        bundleList.Add(newBundle);
                    }
                }
            }

            return bundleList;
        }

        public static void CleanBuiltAssetBundles(AssetBundlePackageBuildSettings[] settings, AssetBundleDistributionProfile distribution, BuildType buildType, BuildTarget buildTarget)
        {
            foreach (var setting in settings)
            {
                if (buildType == BuildType.Local)
                {
                    var path = Path.Combine(Utility.GetLocalOutputPath(setting,distribution), $"{buildTarget}");
                    if(Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
                else if (buildType == BuildType.Remote)
                {
                    var path = Path.Combine(Utility.GetRemoteOutputPath(setting,distribution), $"{buildTarget}");
                    if(Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
            }
        }

        public static void BuildAssetBundles(AssetBundlePackageBuildSettings settings, AssetBundleDistributionProfile distributionProfile, BuildType buildType, bool guiInteractable = true, PlatformType? targetPlatform = null, AssetDependencyTree.ProcessResults dependency = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (distributionProfile == null) throw new ArgumentNullException(nameof(distributionProfile));

            var settingPath = AssetDatabase.GetAssetPath(settings);
            var distributionProfilePath = AssetDatabase.GetAssetPath(distributionProfile);

            if (string.IsNullOrWhiteSpace(settingPath))
                throw new ArgumentException($"{settings.name}({nameof(AssetBundlePackageBuildSettings)}) is not a project asset");
            
            if (string.IsNullOrWhiteSpace(distributionProfilePath))
                throw new ArgumentException($"{settings.name}({nameof(AssetBundleDistributionProfile)}) is not a project asset");
            
            if(!Application.isBatchMode)
            {
                //have to ask save current scene
                var saved = UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

                if(!saved) 
                {
                    EditorUtility.DisplayDialog("Build Failed!", $"User Canceled", "Confirm");
                    return;
                }
            }

            BuildTarget buildTarget = default;
            switch (targetPlatform)
            {
                case PlatformType.Android: buildTarget = BuildTarget.Android; break;
                case PlatformType.IOS: buildTarget = BuildTarget.iOS; break;
                
                case null: 
                default:
                    buildTarget = EditorUserBuildSettings.activeBuildTarget;
                    break;
            }
            
            var bundleList = GetAssetBundlesList(settings);

            var groupTarget = BuildPipeline.GetBuildTargetGroup(buildTarget);

            var outputPath = Utility.CombinePath(buildType == BuildType.Local ? Utility.GetLocalOutputPath(settings, distributionProfile) : Utility.GetRemoteOutputPath(settings, distributionProfile), buildTarget.ToString());

            //generate sharedBundle if needed, and pre generate dependency
            var treeResult = (dependency != null && dependency.ResultsBySettingPath.TryGetValue(settingPath, out var preprocessedTreeResult)) 
                ? preprocessedTreeResult 
                : AssetDependencyTree.ProcessDependencyTree(bundleList);

            if (settings.AutoCreateSharedBundles)
            {
                bundleList.AddRange(treeResult.SharedBundles);
            }

            var buildParams = new CustomBuildParameters(settings, buildTarget, groupTarget, outputPath, treeResult.BundleDependencies, buildType);

            buildParams.UseCache = AssetBundleEditorPrefs.IncrementalBuild;

            if (buildParams.UseCache && settings.UseCacheServer)
            {
                buildParams.CacheServerHost = settings.CacheServerHost;
                buildParams.CacheServerPort = settings.CacheServerPort;
            }

            ContentPipeline.BuildCallbacks.PostPackingCallback += PostPackingForSelectiveBuild;
            var returnCode = ContentPipeline.BuildAssetBundles(buildParams, new BundleBuildContent(bundleList.ToArray()), out var results);
            ContentPipeline.BuildCallbacks.PostPackingCallback -= PostPackingForSelectiveBuild;

            // after build. asset reference might be missed.
            settings = AssetDatabase.LoadAssetAtPath<AssetBundlePackageBuildSettings>(settingPath);
            distributionProfile = AssetDatabase.LoadAssetAtPath<AssetBundleDistributionProfile>(distributionProfilePath);

            if (returnCode == ReturnCode.Success)
            {
                //only remote bundle build generates link.xml
                switch(buildType)
                {
                    case BuildType.Local:
                        WriteManifestFile(outputPath, results, buildTarget, Utility.GetRemoteURL(settings, distributionProfile), settings.name, settings.PackageGuid, settings.DownloadAtInitialTime);
                        WriteLogFile(outputPath, results);
                        if(!Application.isBatchMode && guiInteractable) EditorUtility.DisplayDialog("Build Succeeded!", "Local bundle build succeeded!", "Confirm");
                        break;
                    case BuildType.Remote:
                        WriteManifestFile(outputPath, results, buildTarget, Utility.GetRemoteURL(settings, distributionProfile), settings.name, settings.PackageGuid, settings.DownloadAtInitialTime);
                        WriteLogFile(outputPath, results);
                        var linkPath = TypeLinkerGenerator.Generate(settings, results);
                        if (!Application.isBatchMode && guiInteractable) EditorUtility.DisplayDialog("Build Succeeded!", $"Remote bundle build succeeded, \n {linkPath} updated!", "Confirm");
                        break;
                }
            }
            else
            {
                if(!Application.isBatchMode && guiInteractable) EditorUtility.DisplayDialog("Build Failed!", $"Bundle build failed, \n Code : {returnCode}", "Confirm");
                Debug.LogError(returnCode);
            }
            
            BundlePathCatalog.TruncateAllCatalogs();
        }

        private static ReturnCode PostPackingForSelectiveBuild(IBuildParameters buildParams, IDependencyData dependencyData, IWriteData writeData)
        {
            var customBuildParams = buildParams as CustomBuildParameters;
            var depsDic = customBuildParams.DependencyDic;

            List<string> includedBundles;

            if(customBuildParams.CurrentBuildType == BuildType.Local)
            {
                var assetPaths = new List<string>();
                var loadPaths = new List<string>();
                
                var allFolderPaths = customBuildParams.CurrentSettings.BundleSettings.Select(setting =>
                {
                    //find folder
                    var folderPath = AssetDatabase.GUIDToAssetPath(setting.Folder.guid);
                    if (!AssetDatabase.IsValidFolder(folderPath)) return null;
                    return folderPath;
                }).Where(path => false == string.IsNullOrEmpty(path)).ToArray();
                
                //deps includes every local dependencies recursively
                includedBundles = customBuildParams.CurrentSettings.BundleSettings
                    .Where(setting => setting.IncludedInPlayer)
                    .SelectMany(setting =>
                    {
                        if (setting.SeparateBundlesFileByFile)
                        {
                            var folderPath = AssetDatabase.GUIDToAssetPath(setting.Folder.guid);
                            var subTerritory = allFolderPaths.Where(path => path.StartsWith(folderPath)).ToArray();

                            assetPaths.Clear();
                            loadPaths.Clear();
                            Utility.GetFilesInDirectory(string.Empty, assetPaths, loadPaths, folderPath, setting.IncludeSubfolder, subTerritory, Utility.BuildFileType.ASSETS);
                            var bundleName = Utility.GetBundleNameWithExtension(setting.BundleName);
                            
                            return assetPaths.Select(assetPath =>
                            {
                                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                                var assetName = Path.GetFileNameWithoutExtension(assetPath);
                                return GetSeparatedBundleAssetName(bundleName, assetName, guid);
                            }).ToArray();
                        }
                        else
                        {
                            return new [] { setting.BundleName };
                        }
                    })
                    .SelectMany(bundleName => Utility.CollectBundleDependencies(depsDic, bundleName, true))
                    .Distinct()
                    .ToList();
            }
            //if not local build, we include everything
            else
            {
                includedBundles = depsDic.Keys.ToList();
            }

            //quick exit 
            if (includedBundles == null || includedBundles.Count == 0)
            {
                writeData.WriteOperations.Clear();
                return ReturnCode.Success;
            }

            for (int i = writeData.WriteOperations.Count - 1; i >= 0; --i)
            {
                string bundleName;
                switch (writeData.WriteOperations[i])
                {
                    case SceneBundleWriteOperation sceneOperation:
                        bundleName = sceneOperation.Info.bundleName;
                        break;
                    case SceneDataWriteOperation sceneDataOperation:
                        var bundleWriteData = writeData as IBundleWriteData;
                        bundleName = bundleWriteData.FileToBundle[sceneDataOperation.Command.internalName];
                        break;
                    case AssetBundleWriteOperation assetBundleOperation:
                        bundleName = assetBundleOperation.Info.bundleName;
                        break;
                    default:
                        Debug.LogError("Unexpected write operation");
                        return ReturnCode.Error;
                }

                // if we do not want to build that bundle, remove the write operation from the list
                if (!includedBundles.Contains(bundleName))
                {
                    writeData.WriteOperations.RemoveAt(i);
                }
            }

            return ReturnCode.Success;
        }

        /// <summary>
        /// write manifest into target path.
        /// </summary>
        static void WriteManifestFile(string path, IBundleBuildResults bundleResults, BuildTarget target, string remoteURL, string packageName, string packageGuid, bool downloadAtInitialTime)
        {
            var manifest = new AssetbundleBuildManifest();
            manifest.BuildTarget = target.ToString();

            //we use unity provided dependency result for final check
            var deps = bundleResults.BundleInfos.ToDictionary(kv => kv.Key, kv => kv.Value.Dependencies.ToList());

            foreach (var result in bundleResults.BundleInfos)
            {
                var bundleInfo = new AssetbundleBuildManifest.BundleInfo();
                bundleInfo.BundleName = result.Key;
                bundleInfo.Dependencies = Utility.CollectBundleDependencies(deps, result.Key);
                bundleInfo.Hash = result.Value.Hash;
                bundleInfo.Size = new FileInfo(result.Value.FileName).Length;
                bundleInfo.packageGuid = packageGuid;
                manifest.BundleInfos.Add(bundleInfo);
            }

            //sort by size
            manifest.BundleInfos.Sort((a, b) => b.Size.CompareTo(a.Size));
            var manifestString = JsonUtility.ToJson(manifest);
            manifest.GlobalHash = Hash128.Compute(manifestString);
            manifest.BuildTime = DateTime.UtcNow.Ticks;
            manifest.RemoteURL = remoteURL;
            manifest.DownloadAtInitialTime = downloadAtInitialTime;
            manifest.PackageName = packageName;
            manifest.PackageGuid = packageGuid;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            File.WriteAllText(Utility.CombinePath(path, AssetBundlePackageBuildSettings.ManifestFileName), JsonUtility.ToJson(manifest, true));
        }

        static void WriteSharedBundleLog(string path, AssetDependencyTree.ProcessResult treeResult)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Build Time : {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
            sb.AppendLine($"Possible shared bundles will be created..");
            sb.AppendLine();

            var sharedBundleDic = treeResult.SharedBundles.ToDictionary(ab => ab.assetBundleName, ab => ab.assetNames[0]);

            //find flatten deps which contains non-shared bundles
            var definedBundles = treeResult.BundleDependencies.Keys.Where(name => !sharedBundleDic.ContainsKey(name)).ToList();
            var depsOnlyDefined = definedBundles.ToDictionary(name => name, name => Utility.CollectBundleDependencies(treeResult.BundleDependencies, name));

            foreach(var kv in sharedBundleDic)
            {
                var bundleName = kv.Key;
                var assetPath = kv.Value;
                var referencedDefinedBundles = depsOnlyDefined.Where(pair => pair.Value.Contains(bundleName)).Select(pair => pair.Key).ToList();

                sb.AppendLine($"Shared_{AssetDatabase.AssetPathToGUID(assetPath)}.bundle - { assetPath } is referenced by");
                foreach(var refBundleName in referencedDefinedBundles) sb.AppendLine($"    - {refBundleName}");
                sb.AppendLine();
            }

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            using var writer = File.AppendText(Utility.CombinePath(path, LogExpectedSharedBundleFileName));
            writer.WriteLine(sb.ToString());
        }

        public static string GetExpectedSharedBundleFilesLogPath(string folderPath)
        {
            return Path.GetFullPath(Utility.CombinePath(folderPath, LogExpectedSharedBundleFileName));
        }
        
        public static string GetLogPath(string folderPath)
        {
            return Path.GetFullPath(Utility.CombinePath(folderPath, LogFileName));
        }
        
        /// <summary>
        /// write logs into target path.
        /// </summary>
        static void WriteLogFile(string path, IBundleBuildResults bundleResults)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Build Time : {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
            sb.AppendLine();

            for (int i = 0; i < bundleResults.BundleInfos.Count; i++)
            {
                var bundleInfo = bundleResults.BundleInfos.ElementAt(i);
                var writeResult = bundleResults.WriteResults.ElementAt(i);
                sb.AppendLine($"----File Path : {bundleInfo.Value.FileName}----");
                var assetDic = new Dictionary<string, ulong>();
                foreach(var file in writeResult.Value.serializedObjects)
                {
                    //skip nonassettype
                    if (file.serializedObject.fileType == UnityEditor.Build.Content.FileType.NonAssetType) continue;

                    //gather size
                    var assetPath = AssetDatabase.GUIDToAssetPath(file.serializedObject.guid.ToString());
                    if (!assetDic.ContainsKey(assetPath))
                    {
                        assetDic.Add(assetPath, file.header.size);
                    } 
                    else assetDic[assetPath] += file.header.size;
                }

                //sort by it's size
                var sortedAssets = assetDic.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key);

                foreach(var asset in sortedAssets)
                {
                    sb.AppendLine($"{(asset.Value * 0.000001f).ToString("0.00000").PadLeft(10)} mb - {asset.Key}");
                }

                sb.AppendLine();
            }

            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            File.WriteAllText(GetLogPath(path), sb.ToString());
        }
    }
}
