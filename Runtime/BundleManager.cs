using System;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.Networking;
#if UNITY_IOS || UNITY_IPHONE
using UnityEngine.iOS;
#endif

namespace BundleSystem
{
    /// <summary>
    /// Handle Resources expecially assetbundles.
    /// Also works in editor
    /// </summary>
    public static partial class BundleManager
    {
        //instance is almost only for coroutines
        private static BundleManagerHelper s_Helper { get; set; }
        private static DebugGuiHelper s_DebugGUI { get; set; }

        class LoadedBundle
        {
            public string Name;
            public AssetBundle Bundle;
            public Hash128 Hash;
            public List<string> Dependencies; //including self
            public bool IsLocalBundle;
            public string LoadPath;
            public UnityWebRequest RequestForReload;
            public bool IsReloading = false;
            public LoadedBundle(AssetbundleBuildManifest.BundleInfo info, string loadPath, AssetBundle bundle, bool isLocal)
            {
                Name = info.BundleName;
                IsLocalBundle = isLocal;
                LoadPath = loadPath;
                Bundle = bundle; 
                Hash = info.Hash;
                Dependencies = info.Dependencies;
                Dependencies.Add(Name);
            }

            private BundlePathCatalog _pathCatalog;
            public BundlePathCatalog PathCatalog
            {
                get
                {
                    if (Bundle.isStreamedSceneAssetBundle == false && Bundle != null && _pathCatalog == null)
                    {
                        _pathCatalog = Bundle.LoadAsset<BundlePathCatalog>(nameof(BundlePathCatalog));
                    }
                    return _pathCatalog;
                }
            }
        }

        //Asset bundles that is loaded keep it static so we can easily call this in static method
        static Dictionary<string, LoadedBundle> s_AssetBundles = new Dictionary<string, LoadedBundle>();
        static Dictionary<string, Hash128> s_LocalBundles = new Dictionary<string, Hash128>();
        static Dictionary<string, LoadedBundle> s_SceneNames = new Dictionary<string, LoadedBundle>();

#if UNITY_EDITOR
        public static bool UseAssetDatabase { get; private set; } = true;
#endif
        public static bool Initialized { get; private set; } = false;

        public static bool TryGetLocalURL(string packageGuid, out string url)
        {
            url = default;
#if UNITY_EDITOR
            if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings == null)
            {
                return false;
            }
            else
            {
                url = $"file://{Utility.CombinePath(Application.dataPath, "..", AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetDistributionProfile().localOutputFolder)}/{packageGuid}/{UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString()}";
                return true;
            }
#else
            if (RuntimePackages.Contains(packageGuid))
            {
                url = Utility.CombinePath(AssetBundlePackageBuildSettings.LocalBundleRuntimePath, packageGuid);
                if (Application.platform != RuntimePlatform.Android &&
                    Application.platform != RuntimePlatform.WebGLPlayer)
                {
                    url = $"file://{url}";
                }
                return true;
            }
            else return false;
#endif
        }
        
        private static Dictionary<string, string> s_remoteAssetBundleHomeUriByPackageGuid =
            new Dictionary<string, string>();

        public static bool TryGetRemoteURL(string packageGuid, out string url)
        {
            var result = s_remoteAssetBundleHomeUriByPackageGuid.TryGetValue(packageGuid, out url);
            return result;
        }
        
        internal static int UnityMainThreadId { get; private set; }

        public static bool AutoReloadBundle { get; private set; } = true;
        public static bool LogMessages { get; set; }

#if UNITY_EDITOR && UNITY_2019_1_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void DomainReloaded()
        {
            //need to reset static fields and events
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= TrackOnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= TrackOnSceneUnLoaded;

            //manager defaults
            Initialized = false;
            s_Helper = default;
            s_DebugGUI = default;
            UseAssetDatabase = true;
            s_remoteAssetBundleHomeUriByPackageGuid = new Dictionary<string, string>();
            AutoReloadBundle = true;
            s_LocalBundles.Clear();
            s_SceneNames.Clear();
            OnDestroy(); // need to unload bundles

            //debugging defaults
            ShowDebugGUI = default;
            LogMessages = default;

            //api defaults
            s_EditorAssetMap = default;

            //memory defaults
            s_WeakRefPool.Clear();
            s_BundleRefCounts.Clear();
            s_BundleDirectUseCount.Clear();
            s_TrackingObjects.Clear();
            s_TrackingOwners.Clear();
            s_TrackingGameObjects.Clear();
        }  
#endif

        public static HashSet<string> RuntimePackages = null;
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Setup()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += TrackOnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += TrackOnSceneUnLoaded;
            UnityMainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var managerGo = new GameObject("_BundleManager");
            UnityEngine.Object.DontDestroyOnLoad(managerGo);
            s_Helper = managerGo.AddComponent<BundleManagerHelper>();
            s_DebugGUI = managerGo.AddComponent<DebugGuiHelper>();
            s_DebugGUI.enabled = s_ShowDebugGUI;
#if UNITY_EDITOR
            RuntimePackages =
                new HashSet<string>(
                AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings
                    .GetActiveSettingEntries()
                    .Select(entry=>entry.PackageGuid)
                    .Distinct());
            
            SetupAssetdatabaseUsage();
#else
            RuntimePackages = new HashSet<string>(AssetBundlePackageBuildSettings.ReadRuntimePackageList());
#endif
        }

        static void CollectSceneNames(LoadedBundle loadedBundle)
        {
            var scenes = loadedBundle.Bundle.GetAllScenePaths();
            foreach (var scene in scenes) s_SceneNames[scene] = loadedBundle;
        }

        private static void OnDestroy()
        {
            foreach (var kv in s_AssetBundles)
                kv.Value.Bundle.Unload(false);
            s_AssetBundles.Clear();
        }

        public static BundleAsyncOperation Initialize(bool autoReloadBundle = true)
        {
            var result = new BundleAsyncOperation();
            s_Helper.StartCoroutine(CoInitializeLocalBundles(result, autoReloadBundle));
            return result;
        }

        static IEnumerator CoInitializeLocalBundles(BundleAsyncOperation result, bool autoReloadBundle)
        {
            if(Initialized)
            {
                result.Done(BundleErrorCode.Success);
                yield break;
            }

            AutoReloadBundle = autoReloadBundle;

            s_remoteAssetBundleHomeUriByPackageGuid.Clear();
            
            if(LogMessages) Debug.Log($"Initializing {RuntimePackages.Count} packages.");
            
            foreach (var builtInPackageGuid in RuntimePackages)
            {
                //temp dictionaries to apply very last
                var bundleRequests = new Dictionary<string, UnityWebRequest>();
                var loadedBundles = new Dictionary<string, LoadedBundle>();
                var localBundleHashes = new Dictionary<string, Hash128>();

                if (TryGetLocalURL(builtInPackageGuid, out var localBundleHomePath) == false)
                {
                    if(LogMessages) Debug.Log($"{builtInPackageGuid} is ignored. Unknown package.");
                    continue;
                }
                if(LogMessages) Debug.Log($"LocalBundleHomePath : {localBundleHomePath}");

                var manifestReq = UnityWebRequest.Get(Utility.CombinePath(localBundleHomePath, AssetBundlePackageBuildSettings.ManifestFileName));
                yield return manifestReq.SendWebRequest();

                if(result.IsCancelled) 
                {
                    manifestReq.Dispose();
                    yield break;
                }

                if(!Utility.CheckRequestSuccess(manifestReq))
                {
                    result.Done(BundleErrorCode.NetworkError);
                    yield break;
                }

                if(!AssetbundleBuildManifest.TryParse(manifestReq.downloadHandler.text, out var localManifest))
                {
                    result.Done(BundleErrorCode.ManifestParseError);
                    yield break;
                }

                if(LogMessages) Debug.Log($"Try caching {localManifest.BundleInfos.Count} local bundles.");
                result.SetIndexLength(localManifest.BundleInfos.Count);
                for(int i = 0; i < localManifest.BundleInfos.Count; i++)
                {
                    result.SetCurrentIndex(i);
                    result.SetCachedBundle(true);
                    AssetbundleBuildManifest.BundleInfo bundleInfoToLoad;
                    AssetbundleBuildManifest.BundleInfo bundleInfo = default;
                    var localBundleInfo = localManifest.BundleInfos[i];
                    localBundleHashes.Add(localBundleInfo.BundleName, localBundleInfo.Hash);

                    bool useLocalBundle = 
                        !localManifest.TryGetBundleInfo(localBundleInfo.BundleName, out bundleInfo) ||
                        !Caching.IsVersionCached(bundleInfo.AsCached);

                    bundleInfoToLoad = useLocalBundle ? localBundleInfo : bundleInfo;
                    var loadPath = Utility.CombinePath(localBundleHomePath, bundleInfoToLoad.BundleName);

                    var bundleReq = UnityWebRequestAssetBundle.GetAssetBundle(loadPath, bundleInfoToLoad.Hash);
                    var bundleOp = bundleReq.SendWebRequest();
                    while (!bundleOp.isDone)
                    {
                        result.SetProgress(bundleOp.progress);
                        yield return null;
                        if(result.IsCancelled) break;
                    }

                    if(result.IsCancelled)
                    {
                        bundleReq.Dispose();
                        break;
                    }

                    if(Utility.CheckRequestSuccess(bundleReq))
                    {
                        //load bundle later
                        var loadedBundle = new LoadedBundle(bundleInfoToLoad, loadPath, null, useLocalBundle);
                        bundleRequests.Add(localBundleInfo.BundleName, bundleReq);
                        loadedBundles.Add(localBundleInfo.BundleName, loadedBundle);

                        if (LogMessages) Debug.Log($"Local bundle Loaded - Name : {localBundleInfo.BundleName}, Hash : {bundleInfoToLoad.Hash }");
                    }
                    else
                    {
                        result.Done(BundleErrorCode.NetworkError);
                        yield break;
                    }
                }

                if(result.IsCancelled)
                {
                    foreach(var kv in bundleRequests)
                    {
                        kv.Value.Dispose();
                    }
                    yield break;
                }

                foreach(var kv in s_AssetBundles)
                {
                    kv.Value.Bundle.Unload(false);
                    if (kv.Value.RequestForReload != null) 
                        kv.Value.RequestForReload.Dispose(); //dispose reload bundle
                }

                s_AssetBundles.Clear();
                s_SceneNames.Clear();
                s_LocalBundles = localBundleHashes;

                foreach(var kv in bundleRequests)
                {
                    var loadedBundle = loadedBundles[kv.Key];
                    loadedBundle.Bundle = DownloadHandlerAssetBundle.GetContent(kv.Value);
                    CollectSceneNames(loadedBundle);
                    s_AssetBundles.Add(loadedBundle.Name, loadedBundle);
                    kv.Value.Dispose();
                }
                
                s_remoteAssetBundleHomeUriByPackageGuid[builtInPackageGuid] 
                    = Utility.CombinePath(localManifest.RemoteURL, localManifest.BuildTarget);
#if UNITY_EDITOR
                if (AssetBundleEditorPrefs.EmulateWithoutRemoteURL)
                {
                    var settings = AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetActiveSettingEntries()
                        .FirstOrDefault(setting => setting.PackageGuid == builtInPackageGuid);
                    s_remoteAssetBundleHomeUriByPackageGuid[builtInPackageGuid] 
                        = "file://" + Utility.CombinePath(
                            Utility.GetRemoteOutputPath(settings, AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetDistributionProfile()), 
                            UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString());
                }
#endif
            }
            
            Initialized = true;
            if (LogMessages) Debug.Log($"Initialize Success \nRemote URL : {JsonConvert.SerializeObject(s_remoteAssetBundleHomeUriByPackageGuid)}");
            result.Done(BundleErrorCode.Success);
        }

        /// <summary>
        /// get last cached manifest, to support offline play
        /// </summary>
        /// <returns></returns>
        public static bool TryGetCachedManifest(out AssetbundleBuildManifest manifest)
        {
            return AssetbundleBuildManifest.TryParse(PlayerPrefs.GetString("CachedManifest", string.Empty), out manifest);
        }

        public static BundleAsyncOperation<AssetbundleBuildManifest[]> GetAllManifests()
        {
            var result = new BundleAsyncOperation<AssetbundleBuildManifest[]>();
            s_Helper.StartCoroutine(CoGetManifest(result));
            return result;
        }

        static IEnumerator CoGetManifest(BundleAsyncOperation<AssetbundleBuildManifest[]> result)
        {
            if (!Initialized)
            {
                Debug.LogError("Do Initialize first");
                result.Done(BundleErrorCode.NotInitialized);
                yield break;
            }

#if UNITY_EDITOR
            if (UseAssetDatabase)
            {
                result.Result = new AssetbundleBuildManifest[] { new AssetbundleBuildManifest() };
                result.Done(BundleErrorCode.Success);
                yield break;
            }
#endif

            List<AssetbundleBuildManifest> buildManifests = new List<AssetbundleBuildManifest>();
            
            if (LogMessages) Debug.Log($"Retrieving manifests from {RuntimePackages.Count} packages");
            
            foreach (var packageGuid in RuntimePackages)
            {
                if (TryGetRemoteURL(packageGuid, out var remoteUrl) == false) continue;

                var manifestPath =
                    Utility.CombinePath(remoteUrl, AssetBundlePackageBuildSettings.ManifestFileName)
                    .Replace('\\', '/');
                
                if (LogMessages) Debug.Log($"Try get manifest from {manifestPath}");
                var manifestReq = UnityWebRequest.Get(manifestPath);
                yield return manifestReq.SendWebRequest();

                if(result.IsCancelled)
                {
                    manifestReq.Dispose();
                    yield break;
                }

                if(!Utility.CheckRequestSuccess(manifestReq))
                {
                    result.Done(BundleErrorCode.NetworkError);
                    yield break;
                }

                var remoteManifestJson = manifestReq.downloadHandler.text;
                manifestReq.Dispose();

                if (!AssetbundleBuildManifest.TryParse(remoteManifestJson, out var remoteManifest))
                {
                    result.Done(BundleErrorCode.ManifestParseError);
                    yield break;
                }
                
                if (LogMessages) Debug.Log($"Downloaded Manifest: {remoteManifest.PackageGuid}");

                buildManifests.Add(remoteManifest);
            }

            AllCachedManifests = result.Result = buildManifests.ToArray();
            result.Done(BundleErrorCode.Success);
        }
        
        public static AssetbundleBuildManifest[] AllCachedManifests { get; private set; }

        /// <summary>
        /// Get download size of entire bundles(except cached)
        /// </summary>
        /// <param name="manifest">manifest you get from GetManifest() function</param>
        /// <param name="subsetNames">names that you interested among full bundle list(optional)</param>
        /// <returns></returns>
        public static long GetDownloadSize(AssetbundleBuildManifest[] manifests, IEnumerable<string> subsetNames = null)
        {
            if (!Initialized)
            {
                throw new System.Exception("BundleManager is not initialized");
            }

            long totalSize = 0;

            foreach (var manifest in manifests)
            {
                var bundleInfoList = subsetNames == null ? manifest.BundleInfos : manifest.CollectSubsetBundleInfoes(subsetNames);

                for (int i = 0; i < bundleInfoList.Count; i++)
                {
                    var bundleInfo = bundleInfoList[i];
                    var uselocalBundle = s_LocalBundles.TryGetValue(bundleInfo.BundleName, out var localHash) && localHash == bundleInfo.Hash;
                    if (!uselocalBundle && !Caching.IsVersionCached(bundleInfo.AsCached))
                        totalSize += bundleInfo.Size;
                }
            }

            return totalSize;
        }


        /// <summary>
        /// acutally download assetbundles load from cache if cached 
        /// </summary>
        /// <param name="manifest">manifest you get from GetManifest() function</param>
        /// <param name="subsetNames">names that you interested among full bundle list(optional)</param>
        public static BundleAsyncOperation<bool> DownloadAssetBundles(AssetbundleBuildManifest manifest, IEnumerable<string> subsetNames = null)
        {
            var result = new BundleAsyncOperation<bool>();
            s_Helper.StartCoroutine(CoDownloadAssetBundles(manifest, subsetNames, result));
            return result;
        }

        static IEnumerator CoDownloadAssetBundles(AssetbundleBuildManifest manifest, IEnumerable<string> subsetNames, BundleAsyncOperation<bool> result)
        {
            if (!Initialized)
            {
                Debug.LogError("Do Initialize first");
                result.Done(BundleErrorCode.NotInitialized);
                yield break;
            }

#if UNITY_EDITOR
            if(UseAssetDatabase)
            {
                result.Done(BundleErrorCode.Success);
                yield break;
            }
#endif

            var bundlesToUnload = new HashSet<string>(s_AssetBundles.Keys);
            var downloadBundleList = subsetNames == null ? manifest.BundleInfos : manifest.CollectSubsetBundleInfoes(subsetNames);
            var bundleReplaced = false; //bundle has been replaced
            
            //temp dictionaries to apply very last
            var bundleRequests = new Dictionary<string, UnityWebRequest>();
            var loadedBundles = new Dictionary<string, LoadedBundle>();

            result.SetIndexLength(downloadBundleList.Count);
            
            for (int i = 0; i < downloadBundleList.Count; i++)
            {
                result.SetCurrentIndex(i);
                var bundleInfo = downloadBundleList[i];

                if (TryGetRemoteURL(bundleInfo.packageGuid, out var remoteURL) == false)
                {
                    continue;
                }

                //remove from the set so we can track bundles that should be cleared
                bundlesToUnload.Remove(bundleInfo.BundleName);

                var isLocalBundle = s_LocalBundles.TryGetValue(bundleInfo.BundleName, out var localHash) && localHash == bundleInfo.Hash;
                var isCached = Caching.IsVersionCached(bundleInfo.AsCached);
                result.SetCachedBundle(isCached);

                var loadURL = isLocalBundle 
                    ? TryGetLocalURL(bundleInfo.packageGuid, out var localUrl) 
                        ? Utility.CombinePath(localUrl, bundleInfo.BundleName) 
                        : null 
                    : Utility.CombinePath(remoteURL, bundleInfo.BundleName);
                
                if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName}, loadURL {loadURL}, isLocalBundle : {isLocalBundle}, isCached {isCached}");
                LoadedBundle previousBundle;

                if (s_AssetBundles.TryGetValue(bundleInfo.BundleName, out previousBundle) && previousBundle.Hash == bundleInfo.Hash)
                {
                    if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName} Complete - load skipped");
                }
                else
                {
                    var bundleReq = isLocalBundle ? UnityWebRequestAssetBundle.GetAssetBundle(loadURL) : UnityWebRequestAssetBundle.GetAssetBundle(loadURL, bundleInfo.AsCached);
                    var operation = bundleReq.SendWebRequest();
                    while (!bundleReq.isDone)
                    {
                        result.SetProgress(operation.progress);
                        yield return null;
                        if(result.IsCancelled) break;
                    }

                    if(result.IsCancelled)
                    {
                        bundleReq.Dispose();
                        break;
                    }

                    if(!Utility.CheckRequestSuccess(bundleReq))
                    {
                        result.Done(BundleErrorCode.NetworkError);
                        yield break;
                    }

                    var loadedBundle = new LoadedBundle(bundleInfo, loadURL, null, isLocalBundle);
                    bundleRequests.Add(bundleInfo.BundleName, bundleReq);
                    loadedBundles.Add(bundleInfo.BundleName, loadedBundle);
                    if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName} Complete");
                }
            }

            if(result.IsCancelled)
            {
                foreach(var kv in bundleRequests)
                {
                    kv.Value.Dispose();
                }
                yield break;
            }

            foreach(var kv in bundleRequests)
            {
                var loadedBundle = loadedBundles[kv.Key];
                if (s_AssetBundles.TryGetValue(loadedBundle.Name, out var previousBundle))
                {
                    bundleReplaced = true;
                    previousBundle.Bundle.Unload(false);
                    if (previousBundle.RequestForReload != null) 
                        previousBundle.RequestForReload.Dispose(); //dispose reload bundle
                }
                loadedBundle.Bundle = DownloadHandlerAssetBundle.GetContent(kv.Value);
                CollectSceneNames(loadedBundle);
                s_AssetBundles[loadedBundle.Name] = loadedBundle;
                kv.Value.Dispose();
            }

            //let's drop unknown bundles loaded
            foreach(var name in bundlesToUnload)
            {
                var bundleInfo = s_AssetBundles[name];
                bundleInfo.Bundle.Unload(false);
                if (bundleInfo.RequestForReload != null)
                    bundleInfo.RequestForReload.Dispose(); //dispose reload bundle
                s_AssetBundles.Remove(bundleInfo.Name);
            }

            //bump entire bundles' usage timestamp
            //we use manifest directly to find out entire list
            for (int i = 0; i < manifest.BundleInfos.Count; i++)
            {
                var cachedInfo = manifest.BundleInfos[i].AsCached;
                if (Caching.IsVersionCached(cachedInfo)) Caching.MarkAsUsed(cachedInfo);
            }

            if (LogMessages) Debug.Log($"CacheUsed Before Cleanup : {Caching.defaultCache.spaceOccupied} bytes");
            Caching.ClearCache(600); //as we bumped entire list right before clear, let it be just 600
            if (LogMessages) Debug.Log($"CacheUsed After CleanUp : {Caching.defaultCache.spaceOccupied} bytes");

            PlayerPrefs.SetString("CachedManifest", JsonUtility.ToJson(manifest));
            result.Result = bundleReplaced;
            result.Done(BundleErrorCode.Success);
        }

        public static bool IsCached(AssetbundleBuildManifest target)
        {
            return target.BundleInfos.All(bundle => Caching.IsVersionCached(bundle.AsCached));
        }
        
        /// <summary>
        /// acutally download assetbundles load from cache if cached 
        /// </summary>
        /// <param name="manifests">manifest you get from GetManifest() function</param>
        /// <param name="subsetNames">names that you interested among full bundle list(optional)</param>
        public static BundleAsyncOperation<bool> DownloadAssetBundlesInBackground(AssetbundleBuildManifest[] manifests, IEnumerable<string> subsetNames = null)
        {
            var result = new BundleAsyncOperation<bool>();
            s_Helper.StartCoroutine(CoDownloadAssetBundlesInBackground(manifests, subsetNames, result));
            return result;
        }

        static IEnumerator CoDownloadAssetBundlesInBackground(AssetbundleBuildManifest[] manifests, IEnumerable<string> subsetNames, BundleAsyncOperation<bool> result)
        {
            if (!Initialized)
            {
                Debug.LogError("Do Initialize first");
                result.Done(BundleErrorCode.NotInitialized);
                yield break;
            }

#if UNITY_EDITOR
            if(UseAssetDatabase)
            {
                result.Done(BundleErrorCode.Success);
                yield break;
            }
#endif

            var bundlesToUnload = new HashSet<string>(s_AssetBundles.Keys);
            var downloadBundleList = subsetNames == null 
                ? manifests.SelectMany(manifest=>manifest.BundleInfos).ToList() 
                : manifests.SelectMany(manifest=>manifest.CollectSubsetBundleInfoes(subsetNames)).ToList();
            
            var bundleReplaced = false; //bundle has been replaced
            
            //temp dictionaries to apply very last
            var bundleRequests = new Dictionary<string, UnityWebRequest>();
            var loadedBundles = new Dictionary<string, LoadedBundle>();

            result.SetIndexLength(downloadBundleList.Count);

            const string downloadFolder = ".gameResourcesDownloadTemp";
            string downloadFolderAbsolutePath = Utility.CombinePath(Application.persistentDataPath, downloadFolder);
            if (Directory.Exists(downloadFolderAbsolutePath) == false)
            {
                Directory.CreateDirectory(downloadFolderAbsolutePath);
            }
            
#if UNITY_IOS || UNITY_IPHONE
            Device.SetNoBackupFlag(downloadFolderAbsolutePath);
#endif
            
            List<BackgroundDownloadConfig> settings = new List<BackgroundDownloadConfig>();
            for (int i = 0; i < downloadBundleList.Count; i++)
            {
                var bundleInfo = downloadBundleList[i];
                if (TryGetRemoteURL(bundleInfo.packageGuid, out var remoteUrl) == false)
                {
                    Debug.LogError($"Errored loading remote url from package id:{bundleInfo.packageGuid}");
                    continue;
                }
                
                var isLocalBundle = s_LocalBundles.TryGetValue(bundleInfo.BundleName, out var localHash) && localHash == bundleInfo.Hash;
                if (isLocalBundle) continue; // exclude local bundle files.
                
                var isCached = Caching.IsVersionCached(bundleInfo.AsCached);
                if (isCached)
                {
                    if(LogMessages) Debug.Log($@"{bundleInfo.BundleName}({bundleInfo.Hash}) is cached. skip background downloading");
                    continue;
                }
                
                settings.Add(new BackgroundDownloadConfig
                {
                    url = new Uri(Utility.CombinePath(remoteUrl, bundleInfo.BundleName)),
                    filePath = Utility.CombinePath(downloadFolder, bundleInfo.BundleName),
                });
            }

            if(LogMessages) Debug.Log($"Download assets from {string.Join("\n",settings.Select(setting=>setting.url.ToString()))}");
            
            var downloads = BackgroundDownload.Start(settings.ToArray());
            
            bool IsDownloading()
            {
                int downloading = 0;
                foreach (var dl in downloads)
                {
                    if (dl.status == BackgroundDownloadStatus.Downloading)
                    {
                        downloading++;
                    }
                }

                return downloads.Length > 0 && downloading > 0;
            }

            bool AnyFailure(out BackgroundDownload[] outFailedDownloads)
            {
                List<BackgroundDownload> failedDownloads = null;
                
                foreach (var dl in downloads)
                {
                    if (dl.status == BackgroundDownloadStatus.Failed)
                    {
                        failedDownloads ??= new List<BackgroundDownload>();
                        failedDownloads.Add(dl);
                    }
                }

                outFailedDownloads = failedDownloads?.ToArray();
                return failedDownloads is not null;
            }

            bool TryInterruptIfAnyFailure()
            {
                if (AnyFailure(out var failedDownloads))
                {
                    result.Done(BundleErrorCode.NetworkError);
                    
                    var failedUrls = failedDownloads?.Select(dl => dl.config.url.ToString()).ToArray() ??
                                     Array.Empty<string>();
                    
                    Debug.LogError($"Background downloading failed. stopped downloading process ({string.Join(",", failedUrls)})");

                    foreach (var dl in downloads)
                    {
                        dl.Dispose();
                    }

                    return true;
                }
                return false;
            }

            if (LogMessages) Debug.Log($"Start download files in background");
            
            while (IsDownloading())
            {
                yield return null;
                if (TryInterruptIfAnyFailure()) yield break;
            }
            if (TryInterruptIfAnyFailure()) yield break;
            
            if (LogMessages) Debug.Log($"Complete download files in background ({downloads?.Length})");
            
            for (int i = 0; i < downloadBundleList.Count; i++)
            {
                result.SetCurrentIndex(i);
                var bundleInfo = downloadBundleList[i];

                //remove from the set so we can track bundles that should be cleared
                bundlesToUnload.Remove(bundleInfo.BundleName);

                var isLocalBundle = s_LocalBundles.TryGetValue(bundleInfo.BundleName, out var localHash) && localHash == bundleInfo.Hash;
                var isCached = Caching.IsVersionCached(bundleInfo.AsCached);
                result.SetCachedBundle(isCached);

                var loadURL = isLocalBundle 
                    ? TryGetLocalURL(bundleInfo.packageGuid, out var localUrl) 
                        ? Utility.CombinePath(localUrl, bundleInfo.BundleName) 
                        : null 
                    : Utility.CombinePath(downloadFolderAbsolutePath, bundleInfo.BundleName);

                if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName}, loadURL {loadURL}, isLocalBundle : {isLocalBundle}, isCached {isCached}");

                if (s_AssetBundles.TryGetValue(bundleInfo.BundleName, out var previousBundle) && previousBundle.Hash == bundleInfo.Hash)
                {
                    if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName} Complete - load skipped");
                }
                else
                {
                    var bundleReq = isLocalBundle ? UnityWebRequestAssetBundle.GetAssetBundle(loadURL) : UnityWebRequestAssetBundle.GetAssetBundle($"file://{loadURL}", bundleInfo.AsCached);
                    
                    var operation = bundleReq.SendWebRequest();
                    while (!bundleReq.isDone)
                    {
                        result.SetProgress(operation.progress);
                        yield return null;
                        if(result.IsCancelled) break;
                    }

                    if(result.IsCancelled)
                    {
                        bundleReq.Dispose();
                        break;
                    }

                    if(!Utility.CheckRequestSuccess(bundleReq))
                    {
                        result.Done(BundleErrorCode.NetworkError);
                        yield break;
                    }

                    if (isLocalBundle == false)
                    {
                        // delete after cached.
                        var filePath = Utility.CombinePath(downloadFolderAbsolutePath, bundleInfo.BundleName);
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                            if (LogMessages) Debug.Log($"Deleted temporal downloaded file: {bundleInfo.BundleName}");
                        }
                    }

                    var loadedBundle = new LoadedBundle(bundleInfo, loadURL, null, isLocalBundle);
                    bundleRequests.Add(bundleInfo.BundleName, bundleReq);
                    loadedBundles.Add(bundleInfo.BundleName, loadedBundle);
                    if (LogMessages) Debug.Log($"Loading Bundle Name : {bundleInfo.BundleName} Complete");
                }
            }

            if(result.IsCancelled)
            {
                foreach(var kv in bundleRequests)
                {
                    kv.Value.Dispose();
                }
                yield break;
            }

            foreach(var kv in bundleRequests)
            {
                var loadedBundle = loadedBundles[kv.Key];
                if (s_AssetBundles.TryGetValue(loadedBundle.Name, out var previousBundle))
                {
                    bundleReplaced = true;
                    previousBundle.Bundle.Unload(false);
                    if (previousBundle.RequestForReload != null) 
                        previousBundle.RequestForReload.Dispose(); //dispose reload bundle
                }
                loadedBundle.Bundle = DownloadHandlerAssetBundle.GetContent(kv.Value);
                CollectSceneNames(loadedBundle);
                s_AssetBundles[loadedBundle.Name] = loadedBundle;
                kv.Value.Dispose();
            }

            //let's drop unknown bundles loaded
            foreach(var name in bundlesToUnload)
            {
                var bundleInfo = s_AssetBundles[name];
                bundleInfo.Bundle.Unload(false);
                if (bundleInfo.RequestForReload != null)
                    bundleInfo.RequestForReload.Dispose(); //dispose reload bundle
                s_AssetBundles.Remove(bundleInfo.Name);
            }

            //bump entire bundles' usage timestamp
            //we use manifest directly to find out entire list
            for (int j = 0; j < manifests.Length; j++)
            for (int i = 0; i < manifests[j].BundleInfos.Count; i++)
            {
                var cachedInfo = manifests[j].BundleInfos[i].AsCached;
                if (Caching.IsVersionCached(cachedInfo)) Caching.MarkAsUsed(cachedInfo);
            }

            if (LogMessages) Debug.Log($"CacheUsed Before Cleanup : {Caching.defaultCache.spaceOccupied} bytes");
            Caching.ClearCache(600); //as we bumped entire list right before clear, let it be just 600
            if (LogMessages) Debug.Log($"CacheUsed After CleanUp : {Caching.defaultCache.spaceOccupied} bytes");

            PlayerPrefs.SetString("CachedManifest", JsonUtility.ToJson(manifests));
            result.Result = bundleReplaced;
            result.Done(BundleErrorCode.Success);
        }

        //helper class for coroutine and callbacks
        private class BundleManagerHelper : MonoBehaviour
        {
            private void Update()
            {
                BundleManager.Update();
            }

            private void OnDestroy()
            {
                BundleManager.OnDestroy();
            }
        }
    }
}
