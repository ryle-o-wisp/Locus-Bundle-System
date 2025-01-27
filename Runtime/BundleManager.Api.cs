﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BundleSystem
{
    public static partial class BundleManager
    {
#if UNITY_EDITOR
        private static EditorAssetMap s_EditorAssetMap;

        static void SetupAssetdatabaseUsage()
        {
            if (AssetBundleEditorPrefs.CleanCacheInEditor)
            {
                Caching.ClearCache();
            }

            UseAssetDatabase = !AssetBundleEditorPrefs.EmulateInEditor;

            //create editor asset map
            if(UseAssetDatabase)
            {
                s_EditorAssetMap = new EditorAssetMap(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetActiveSettingEntries());
                //set initialied so it does not need explit call initialzed when using aassetdatabase
                Initialized = true;
            }
        }

        public static void SetupApiTestSettings(AssetBundlePackageBuildSettings[] settings = null)
        {
            if(Application.isPlaying) throw new System.Exception("This function cannot be called while playing!");
            if(settings == null || settings.Any(setting => setting.IsValid() == false)) throw new System.Exception("AssetbundleBuildSetting is not valid");
            UseAssetDatabase = true;
            //create editor asset map only for testing
            s_EditorAssetMap = new EditorAssetMap(settings);
        }

        private static void EnsureAssetDatabase()
        {
            if(!Application.isPlaying && s_EditorAssetMap == null) throw new System.Exception("EditorAssetMap is null, try call SetupApiTestSettings before calling actual api in non-play mode");
        }
#endif

        public static T[] LoadAll<T>(string bundleName) where T : Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabase)
            {
                EnsureAssetDatabase();
                var assets = s_EditorAssetMap.GetAssetPaths(bundleName);
                if (assets.Length == 0) return System.Array.Empty<T>();

                var typeExpected = typeof(T);
                var foundList = new List<T>(assets.Length);

                for (int i = 0; i < assets.Length; i++)
                {
                    var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assets[i]);
                    if (loaded == null) continue;
                    foundList.Add(loaded);
                }
                return foundList.ToArray();
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return new T[0];
            var loadedAssets = foundBundle.Bundle.LoadAllAssets<T>();
            TrackObjectsInternal(loadedAssets, foundBundle);
            return loadedAssets;
        }


        public static T Load<T>(string bundleName, string assetName, bool acceptTypeInherited = false) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                var assetPath = s_EditorAssetMap.GetAssetPath<T>(bundleName, assetName);
                if(string.IsNullOrEmpty(assetPath)) return null; //asset not exist
                return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return null;

            if (acceptTypeInherited)
            {
                var loadedObjectAsset = foundBundle.Bundle.LoadAsset(assetName) as T;
                if(loadedObjectAsset != null) TrackObjectInternal(loadedObjectAsset, foundBundle);
                return loadedObjectAsset;
            }
            else
            {
                var loadedAsset = foundBundle.Bundle.LoadAsset<T>(assetName);
                if(loadedAsset != null) TrackObjectInternal(loadedAsset, foundBundle);
                return loadedAsset;
            }
        }

        public static T LoadByGuid<T>(string guid, bool acceptTypeInherited = false) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabase)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return null; // not exist.
                return AssetDatabase.LoadAssetAtPath<T>(path);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            using var iter = s_AssetBundles.GetEnumerator();
            while (iter.MoveNext())
            {
                var kvp = iter.Current;
                var bundleName = kvp.Key;
                var loadedBundle = kvp.Value;
                var pathCatalog = loadedBundle.PathCatalog;
                if (loadedBundle.Bundle.isStreamedSceneAssetBundle == false && pathCatalog != null && pathCatalog.TryGetMainAssetPath(guid, out var path))
                {
                    return Load<T>(bundleName, path, acceptTypeInherited);
                }
            }
            return null;
        }
        
        public static T LoadByGuidAndLocalId<T>(string guid, long localId) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabase)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return null; // not exist.
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var loadedGuid,
                            out long loadedLocalId) && localId == loadedLocalId)
                    {
                        return asset as T;
                    }
                }
                return null;
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            using var iter = s_AssetBundles.GetEnumerator();
            while (iter.MoveNext())
            {
                var kvp = iter.Current;
                var bundleName = kvp.Key;
                var pathCatalog = kvp.Value.PathCatalog;
                if (pathCatalog != null && pathCatalog.TryGetSubAssetPath(guid, localId, out var path, out var assetName))
                {
                    var assets = LoadWithSubAssets<T>(bundleName, path);
                    for (int i = 0; i < assets.Length; i++)
                    {
                        var asset = assets[i];
                        if (asset.name == assetName)
                        {
                            return asset;
                        }
                    }
                }
            }
            return null;
        }
        
        public static T[] LoadWithSubAssets<T>(string bundleName, string assetName) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                var assetPath = s_EditorAssetMap.GetAssetPath<T>(bundleName, assetName);
                if(string.IsNullOrEmpty(assetPath)) return new T[0];
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
                return assets.Select(a => a as T).Where(a => a != null).ToArray();
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return new T[0];
            var loadedAssets = foundBundle.Bundle.LoadAssetWithSubAssets<T>(assetName);
            TrackObjectsInternal(loadedAssets, foundBundle);
            return loadedAssets;
        }


        public static BundleRequest<T> LoadAsync<T>(string bundleName, string assetName, bool acceptTypeInherited = false) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                var assetPath = s_EditorAssetMap.GetAssetPath<T>(bundleName, assetName);
                if(string.IsNullOrEmpty(assetPath)) return new BundleRequest<T>((T)null); //asset not exist
                return new BundleRequest<T>(UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath));
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return new BundleRequest<T>((T)null); //asset not exist
            var request = 
                acceptTypeInherited
                ? foundBundle.Bundle.LoadAssetAsync(assetName)
                : foundBundle.Bundle.LoadAssetAsync<T>(assetName);
            //need to keep bundle while loading, so we retain before load, release after load
            RetainBundleInternal(foundBundle, 1);
            request.completed += op => AsyncAssetLoaded(request, foundBundle);
            return new BundleRequest<T>(request);
        }
        
        public static BundleRequest<T> LoadByGuidAsync<T>(string guid, bool acceptTypeInherited = false) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return null; // not exist.
                return new BundleRequest<T>(UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path));
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            
            using var iter = s_AssetBundles.GetEnumerator();
            while (iter.MoveNext())
            {
                var kvp = iter.Current;
                var bundleName = kvp.Key;
                var pathCatalog = kvp.Value.PathCatalog;
                if (pathCatalog != null && pathCatalog.TryGetMainAssetPath(guid, out var path))
                {
                    if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return new BundleRequest<T>((T)null); //asset not exist
                    var request = 
                        acceptTypeInherited
                            ? foundBundle.Bundle.LoadAssetAsync(path)
                            : foundBundle.Bundle.LoadAssetAsync<T>(path);
                    //need to keep bundle while loading, so we retain before load, release after load
                    RetainBundleInternal(foundBundle, 1);
                    request.completed += op => AsyncAssetLoaded(request, foundBundle);
                    return new BundleRequest<T>(request);
                }
            }
            return new BundleRequest<T>((AssetBundleRequest)null);
        }
        
        public static BundleRequest<T> LoadByGuidAndLocalIdAsync<T>(string guid, long localId) where T : UnityEngine.Object
        {   
#if UNITY_EDITOR
            if (UseAssetDatabase)
            {
                EnsureAssetDatabase();
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return null; // not exist.
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var loadedGuid,
                            out long loadedLocalId) && localId == loadedLocalId)
                    {
                        return new BundleRequest<T>(asset as T);
                    }
                }
                return new BundleRequest<T>((T)null);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            
            using var iter = s_AssetBundles.GetEnumerator();
            while (iter.MoveNext())
            {
                var kvp = iter.Current;
                var bundleName = kvp.Key;
                var pathCatalog = kvp.Value.PathCatalog;
                if (pathCatalog != null && pathCatalog.TryGetSubAssetPath(guid, localId, out var path, out var assetName))
                {
                    if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return new BundleRequest<T>((T)null); //asset not exist
                    var request = foundBundle.Bundle.LoadAssetWithSubAssetsAsync<T>(path);
                    //need to keep bundle while loading, so we retain before load, release after load
                    RetainBundleInternal(foundBundle, 1);
                    request.completed += op => AsyncAssetLoaded(request, foundBundle);
                    return new BundleRequest<T>(request, assetName);
                }
            }
            return new BundleRequest<T>((AssetBundleRequest)null);
        }
        
        public static BundleRequest<T> LoadAsync<T>(string bundleName, string assetName, string subAssetName) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                var assetPath = s_EditorAssetMap.GetAssetPath<T>(bundleName, assetName);
                if(string.IsNullOrEmpty(assetPath)) return new BundleRequest<T>((T)null); //asset not exist
                return new BundleRequest<T>(UnityEditor.AssetDatabase.LoadAssetAtPath<T>(assetPath));
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return new BundleRequest<T>((T)null); //asset not exist
            var request = foundBundle.Bundle.LoadAssetWithSubAssetsAsync<T>(assetName);
            //need to keep bundle while loading, so we retain before load, release after load
            RetainBundleInternal(foundBundle, 1);
            request.completed += op => AsyncAssetLoaded(request, foundBundle);
            return new BundleRequest<T>(request, subAssetName);
        }

        private static void AsyncAssetLoaded(AssetBundleRequest request, LoadedBundle loadedBundle)
        {
            if(request.asset != null)
            {
                TrackObjectInternal(request.asset, loadedBundle);
            }
            
            //because we did increase ref count in loadasync function, it need to be released
            ReleaseBundleInternal(loadedBundle, 1);
        }

        public static void LoadScene(string bundleName, string sceneName, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) throw new System.Exception("This function does not support non-playing mode!");
            if (UseAssetDatabase)
            {
                EnsureAssetDatabase();
                var scenePath = s_EditorAssetMap.GetScenePath(bundleName, sceneName);
                if(string.IsNullOrEmpty(scenePath)) return; // scene does not exist
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(mode));
                return;
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            SceneManager.LoadScene(Path.GetFileName(sceneName), mode);
        }
        
        public static AsyncOperation LoadSceneAsync(string bundleName, string sceneName, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) throw new System.Exception("This function does not support non-playing mode!");
            if (UseAssetDatabase)
            {
                EnsureAssetDatabase();
                var scenePath = s_EditorAssetMap.GetScenePath(bundleName, sceneName);
                if(string.IsNullOrEmpty(scenePath)) return null; // scene does not exist
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(mode));
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");

            //like default scene load functionality, we return null if something went wrong
            if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) 
            {
                Debug.LogError("Bundle you requested could not be found");
                return null;
            }

            //need to keep bundle while loading, so we retain before load, release after load
            var aop = SceneManager.LoadSceneAsync(Path.GetFileName(sceneName), mode);
            if(aop != null)
            {
                RetainBundleInternal(foundBundle, 1);
                aop.completed += op => ReleaseBundleInternal(foundBundle, 1);
            }

            return aop;
        }

        
        public static void LoadScene(string scenePath, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) throw new System.Exception("This function does not support non-playing mode!");
            if (UseAssetDatabase)
            {
                EnsureAssetDatabase();
                if(s_EditorAssetMap.IsValidScene(scenePath) == false) return; // scene does not exist
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(mode));
                return;
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            
            if (s_SceneNames.TryGetValue(scenePath, out var foundBundle) == false)
            {
                Debug.LogError("Bundle you requested could not be found");
                return;
            }

            SceneManager.LoadScene(scenePath, mode);
        }
        
        public static AsyncOperation LoadSceneAsync(string scenePath, LoadSceneMode mode)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) throw new System.Exception("This function does not support non-playing mode!");
            if (UseAssetDatabase)
            {
                EnsureAssetDatabase();
                if(s_EditorAssetMap.IsValidScene(scenePath) == false) return null; // scene does not exist
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(mode));
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");

            if (s_SceneNames.TryGetValue(scenePath, out var foundBundle) == false)
            {
                Debug.LogError("Bundle you requested could not be found");
                return null;
            }

            //need to keep bundle while loading, so we retain before load, release after load
            var aop = SceneManager.LoadSceneAsync(scenePath, mode);
            if(aop != null)
            {
                RetainBundleInternal(foundBundle, 1);
                aop.completed += op => ReleaseBundleInternal(foundBundle, 1);
            }

            return aop;
        }

        
        public static bool IsAssetExist(string bundleName, string assetName)
        {
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                return s_EditorAssetMap.IsAssetExist(bundleName, assetName);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle)) return false;
            return foundBundle.Bundle.Contains(assetName);
        }

        public static bool IsAssetExistByGuid(string guid)
        {   
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                var path = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(path) == false;
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            
            using var iter = s_AssetBundles.GetEnumerator();
            while (iter.MoveNext())
            {
                var kvp = iter.Current;
                var pathCatalog = kvp.Value.PathCatalog;
                if (pathCatalog != null && pathCatalog.ContainsGuid(guid))
                {
                    return true;
                }
            }

            return false;
        }
        
        public static bool IsAssetExistByGuidAndLocalId(string guid, long localId)
        {   
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) return false;
                var allAssets = AssetDatabase.LoadAllAssetsAtPath(guid);
                return allAssets?
                    .Any(asset => 
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long readLocalId) 
                        && readLocalId == localId) ?? false;
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            
            using var iter = s_AssetBundles.GetEnumerator();
            while (iter.MoveNext())
            {
                var kvp = iter.Current;
                var pathCatalog = kvp.Value.PathCatalog;
                if (pathCatalog != null && pathCatalog.TryGetSubAssetPath(guid, localId, out _, out _))
                {
                    return true;
                }
            }

            return false;
        }

        public static string[] GetAssetNames(string bundleName) 
        {
#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                return s_EditorAssetMap.GetAssetNames(bundleName);
            }
#endif
            if (!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            if (!s_AssetBundles.TryGetValue(bundleName, out var foundBundle))
                return new string[0];
            return foundBundle.Bundle.GetAllAssetNames();
        }
        
        public static GameObject Instantiate(GameObject original)
        {
            if(original == null) throw new System.Exception("The gameobject you want instantiate is null");

#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                return GameObject.Instantiate(original);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            var id = original.GetInstanceID();
            if (original.scene.name != null || !s_TrackingObjects.TryGetValue(id, out var tracking)) throw new System.Exception("Object must be valid bundle object");
            var instantiated = GameObject.Instantiate(original);
            var tupleKey = new TupleObjectKey(instantiated, original);
            s_TrackingOwners.Add(tupleKey, new TrackingOwner(instantiated, original));
            tracking.RefCount++; //increase refCount
            s_TrackingObjects[id] = tracking;
            return instantiated;
        }

        public static GameObject Instantiate(GameObject original, Transform parent)
        {
            if(original == null) throw new System.Exception("The gameobject you want instantiate is null");
            if(parent == null) throw new System.Exception("The parent transform is null");

#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                return GameObject.Instantiate(original, parent);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            var id = original.GetInstanceID();
            if (original.scene.name != null || !s_TrackingObjects.TryGetValue(id, out var tracking)) throw new System.Exception("Object must be valid bundle object");
            var instantiated = GameObject.Instantiate(original, parent);
            var tupleKey = new TupleObjectKey(instantiated, original);
            s_TrackingOwners.Add(tupleKey, new TrackingOwner(instantiated, original));
            tracking.RefCount++; //increase refCount
            s_TrackingObjects[id] = tracking;
            return instantiated;
        }

        public static GameObject Instantiate(GameObject original, Transform parent, bool instantiateInWorldSpace)
        {
            if(original == null) throw new System.Exception("The gameobject you want instantiate is null");
            if(parent == null) throw new System.Exception("The parent transform is null");

#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                return GameObject.Instantiate(original, parent, instantiateInWorldSpace);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            var id = original.GetInstanceID();
            if (original.scene.name != null || !s_TrackingObjects.TryGetValue(id, out var tracking)) throw new System.Exception("Object must be valid bundle object");
            var instantiated = GameObject.Instantiate(original, parent, instantiateInWorldSpace);
            var tupleKey = new TupleObjectKey(instantiated, original);
            s_TrackingOwners.Add(tupleKey, new TrackingOwner(instantiated, original));
            tracking.RefCount++; //increase refCount
            s_TrackingObjects[id] = tracking;
            return instantiated;
        }

        public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation)
        {
            if(original == null) throw new System.Exception("The gameobject you want instantiate is null");

#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                return GameObject.Instantiate(original, position, rotation);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            var id = original.GetInstanceID();
            if (original.scene.name != null || !s_TrackingObjects.TryGetValue(id, out var tracking)) throw new System.Exception("Object must be valid bundle object");
            var instantiated = GameObject.Instantiate(original, position, rotation);
            var tupleKey = new TupleObjectKey(instantiated, original);
            s_TrackingOwners.Add(tupleKey, new TrackingOwner(instantiated, original));
            tracking.RefCount++; //increase refCount
            s_TrackingObjects[id] = tracking;
            return instantiated;
        }

        public static GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation, Transform parent)
        {
            if(original == null) throw new System.Exception("The gameobject you want instantiate is null");
            if(parent == null) throw new System.Exception("The parent transform is null");

#if UNITY_EDITOR
            if (UseAssetDatabase) 
            {
                EnsureAssetDatabase();
                return GameObject.Instantiate(original, position, rotation, parent);
            }
#endif
            if(!Initialized) throw new System.Exception("BundleManager not initialized, try initialize first!");
            var id = original.GetInstanceID();
            if (original.scene.name != null || !s_TrackingObjects.TryGetValue(id, out var tracking)) throw new System.Exception("Object must be valid bundle object");
            var instantiated = GameObject.Instantiate(original, position, rotation, parent);
            var tupleKey = new TupleObjectKey(instantiated, original);
            s_TrackingOwners.Add(tupleKey, new TrackingOwner(instantiated, original));
            tracking.RefCount++; //increase refCount
            s_TrackingObjects[id] = tracking;
            return instantiated;
        }
    }

    /// <summary>
    /// this class is for simulating assetbundle request in editor.
    /// using this class we can provide unified structure.
    /// </summary>
    public class BundleRequest<T> : CustomYieldInstruction, IAwaiter<BundleRequest<T>>, System.IDisposable where T : Object
    {
        AssetBundleRequest mRequest;
        T mLoadedAsset;
        private int? _loadedAssetInstanceId;
        private readonly string _subAssetName;

        /// <summary>
        /// actual assetbundle request warpper
        /// </summary>
        /// <param name="request"></param>
        public BundleRequest(AssetBundleRequest request)
        {
            mRequest = request;
            _subAssetName = null;
        }
        
        public BundleRequest(AssetBundleRequest request, string subAssetName)
        {
            mRequest = request;
            _subAssetName = subAssetName;
        }

        private readonly bool _releaseObjectOnDispose;

        /// <summary>
        /// create already ended bundle request for editor use
        /// </summary>
        /// <param name="loadedAsset"></param>
        /// <param name="releaseObjectOnDispose">Release Object on dispose</param>
        public BundleRequest(T loadedAsset, bool releaseObjectOnDispose = false)
        {
            mLoadedAsset = loadedAsset;
            _loadedAssetInstanceId = loadedAsset != null ? loadedAsset.GetInstanceID() : null;
            _subAssetName = null;
            _releaseObjectOnDispose = releaseObjectOnDispose;
        }

        //provide similar apis
        public override bool keepWaiting => mRequest == null ? false : !mRequest.isDone;
        public bool IsDone => mRequest == null ? true : mRequest.isDone;
        bool IAwaiter<BundleRequest<T>>.IsCompleted => IsDone;
        public T Asset => mRequest == null ? mLoadedAsset : _subAssetName != null ? mRequest.allAssets.FirstOrDefault(asset=>asset.name == _subAssetName) as T : mRequest.asset as T;
        public float Progress => mRequest == null ? 1f : mRequest.progress;

        public void Dispose()
        {
            if(mRequest != null)
            {
                if(mRequest.isDone)
                {
                    if (mRequest.asset != null) BundleManager.ReleaseObject(mRequest.asset);
                }
                else
                {
                    mRequest.completed += op =>
                    {
                        if(mRequest.asset != null) BundleManager.ReleaseObject(mRequest.asset);
                    };
                }
            }
            else if (_releaseObjectOnDispose)
            {
                if (_loadedAssetInstanceId.HasValue)
                {
                    BundleManager.ReleaseObject(_loadedAssetInstanceId.Value);
                }
            }
        }

        BundleRequest<T> IAwaiter<BundleRequest<T>>.GetResult() => this;
        
        public IAwaiter<BundleRequest<T>> GetAwaiter() => this;
        
        void INotifyCompletion.OnCompleted(System.Action continuation)
        {
            if(Thread.CurrentThread.ManagedThreadId != BundleManager.UnityMainThreadId) 
            {
                throw new System.Exception("Should be awaited in UnityMainThread"); 
            }

            if(IsDone) continuation.Invoke();
            else mRequest.completed += op => continuation.Invoke();
        }

        void ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation) => ((INotifyCompletion)this).OnCompleted(continuation);
    }

    /// <summary>
    /// Async operation of BundleManager. with result T
    /// </summary>
    public class BundleAsyncOperation<T> : BundleAsyncOperationBase, IAwaiter<BundleAsyncOperation<T>>
    {
        public T Result { get; internal set; }

        //awaiter implementations
        bool IAwaiter<BundleAsyncOperation<T>>.IsCompleted => IsDone;
        BundleAsyncOperation<T> IAwaiter<BundleAsyncOperation<T>>.GetResult() => this;
        public IAwaiter<BundleAsyncOperation<T>> GetAwaiter() => this;
        void INotifyCompletion.OnCompleted(System.Action continuation) => AwaiterOnComplete(continuation);
        void ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation) => AwaiterOnComplete(continuation);
    }

    /// <summary>
    /// Async operation of BundleManager.
    /// </summary>
    public class BundleAsyncOperation : BundleAsyncOperationBase, IAwaiter<BundleAsyncOperation>
    {
        //awaiter implementations
        bool IAwaiter<BundleAsyncOperation>.IsCompleted => IsDone;
        BundleAsyncOperation IAwaiter<BundleAsyncOperation>.GetResult() => this;
        public IAwaiter<BundleAsyncOperation> GetAwaiter() => this;
        void INotifyCompletion.OnCompleted(System.Action continuation) => AwaiterOnComplete(continuation);
        void ICriticalNotifyCompletion.UnsafeOnCompleted(System.Action continuation) => AwaiterOnComplete(continuation);
    }
    

    /// <summary>
    /// Base class of async bundle operation
    /// </summary>
    public class BundleAsyncOperationBase : CustomYieldInstruction
    {
        /// <summary>
        /// Whether this operation has completed or not.
        /// </summary>
        public bool IsDone => ErrorCode != BundleErrorCode.NotFinished;
        /// Whether this operation has succeeded or not.
        public bool Succeeded => ErrorCode == BundleErrorCode.Success;
        /// ErrorCode of the operation.
        public BundleErrorCode ErrorCode { get; private set; } = BundleErrorCode.NotFinished;
        /// <summary>
        /// Total Assetbundle Count to precess
        /// </summary>
        public int TotalCount { get; private set; } = 0;
        /// <summary>
        /// Precessed assetbundle count along with TotalCount
        /// -1 means processing has not been started.
        /// </summary>
        public int CurrentCount { get; private set; } = -1;
        /// <summary>
        /// Total progress of this operation. From 0 to 1.
        /// </summary>
        public float Progress { get; private set; } = 0f;
        /// <summary>
        /// Whether currently processing AssetBundle is loading from cache or downloading.
        /// </summary>
        public bool CurrentlyLoadingFromCache { get; private set; } = false;

        /// <summary>
        /// Is operation cancelled
        /// </summary>
        public bool IsCancelled => ErrorCode == BundleErrorCode.Cancelled;

        protected event System.Action m_OnComplete;
        /// <summary>
        /// Whether this operation has completed or not.
        /// </summary>
        public override bool keepWaiting => !IsDone;

        internal void SetCachedBundle(bool cached)
        {
            CurrentlyLoadingFromCache = cached;
        }

        internal void SetIndexLength(int total)
        {
            TotalCount = total;
        }

        internal void SetCurrentIndex(int current)
        {
            CurrentCount = current;
        }

        internal void SetProgress(float progress)
        {
            Progress = progress;
        }

        internal void Done(BundleErrorCode code)
        {
            if (code == BundleErrorCode.Success)
            {
                CurrentCount = TotalCount;
                Progress = 1f;
            }
            ErrorCode = code;
            m_OnComplete?.Invoke();
            m_OnComplete = null;
        }

        protected void AwaiterOnComplete(System.Action continuation)
        {
            if(Thread.CurrentThread.ManagedThreadId != BundleManager.UnityMainThreadId) 
            {
                throw new System.Exception("Should be awaited in UnityMainThread"); 
            }

            if(IsDone) continuation.Invoke();
            else m_OnComplete += continuation;
        }

        public void Cancel()
        {
            if(IsDone) throw new System.Exception("Operation has been dont. can't be cancelled");
            ErrorCode = BundleErrorCode.Cancelled;
        }
    }

    public enum BundleErrorCode
    {
        NotFinished = -1,
        Success = 0,
        NotInitialized = 1,
        NetworkError = 2,
        ManifestParseError = 3,
        Cancelled = 4,
    }
    
    /// <summary>
    /// Helper interface for async/await functionality
    /// </summary>
    public interface IAwaiter<out TResult> : ICriticalNotifyCompletion
    {
        bool IsCompleted { get; }

        TResult GetResult();

        IAwaiter<TResult> GetAwaiter();
    }
}