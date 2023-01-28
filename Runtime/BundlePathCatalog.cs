using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.VersionControl;
#endif
using UnityEngine;

namespace BundleSystem
{
    public class BundlePathCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string path;
            public bool isMainAsset;
            public string subAssetName;
            public string guid;
            public long localId;
        }

        public Entry[] entries = null;

        private Dictionary<string, Entry[]> _pathToSubEntries = null;
        private Dictionary<string, Entry[]> _guidToSubEntries = null;
        private Dictionary<string, Entry> _pathToMainEntry = null;
        private Dictionary<string, Entry> _guidToMainEntry = null;
        
        private void EnsureCacheMap()
        {
            if (_pathToSubEntries is null)
            {
                var map = new Dictionary<string, Entry[]>();
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.path)) continue;
                    if (entry.isMainAsset) continue;
                    
                    if (map.TryGetValue(entry.path, out var entriesOfGuid))
                    {
                        map[entry.path] = entriesOfGuid.Append(entry).ToArray();
                    }
                    else
                    {
                        map[entry.path] = new[] { entry };
                    }
                }
                _pathToSubEntries = map;
            }

            if (_guidToSubEntries is null)
            {
                var map = new Dictionary<string, Entry[]>();
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.guid)) continue;
                    if (entry.isMainAsset) continue;
                    
                    if (map.TryGetValue(entry.guid, out var entriesOfGuid))
                    {
                        map[entry.guid] = entriesOfGuid.Append(entry).ToArray();
                    }
                    else
                    {
                        map[entry.guid] = new[] { entry };
                    }
                }
                _guidToSubEntries = map;
            }

            if (_pathToMainEntry is null)
            {   
                var map = new Dictionary<string, Entry>();
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.path)) continue;
                    if (entry.isMainAsset == false) continue;
                    map[entry.path] = entry;
                }
                _pathToMainEntry = map;
            }
            
            if (_guidToMainEntry is null)
            {   
                var map = new Dictionary<string, Entry>();
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.path)) continue;
                    if (entry.isMainAsset == false) continue;
                    map[entry.guid] = entry;
                }
                _guidToMainEntry = map;
            }
        }

        public bool TryGetMainAssetPath(string guid, out string path)
        {
            EnsureCacheMap();
            path = default;
            
            if (_guidToMainEntry.TryGetValue(guid, out var entry))
            {
                path = entry.path;
                return true;
            }
            else return false;
        }
        
        public bool TryGetMainAssetGuid(string path, out string guid)
        {
            EnsureCacheMap();
            guid = default;
            
            if (_guidToMainEntry.TryGetValue(path, out var entry))
            {
                guid = entry.guid;
                return true;
            }
            else return false;
        }

        public bool ContainsGuid(string guid)
        {
            return _guidToMainEntry.ContainsKey(guid) || _guidToSubEntries.ContainsKey(guid);
        }
        
        public bool TryGetSubAssetPath(string guid, long localId, out string path, out string assetName)
        {
            EnsureCacheMap();
            path = default;
            assetName = default;
            
            if (_guidToSubEntries.TryGetValue(guid, out var entries))
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].localId == localId)
                    {
                        path = entries[i].path;
                        assetName = entries[i].subAssetName;
                        return true;
                    }
                }
                return false;
            }
            else return false;
        }
        
        public bool TryGetSubAssetId(string path, string assetName, out string guid, out long localId)
        {
            EnsureCacheMap();
            guid = default;
            localId = default;
            
            if (_pathToSubEntries.TryGetValue(path, out var entries))
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].subAssetName == assetName)
                    {
                        guid = entries[i].guid;
                        localId = entries[i].localId;
                        return true;
                    }
                }
                return false;
            }
            else return false;
        }
        
        private const string CatalogName = "BundlePathCatalog.asset";
        
#if UNITY_EDITOR
        public static readonly HashSet<string> ExcludeExtensions = new HashSet<string>
        {
            ".cs", ".so", ".a", ".dll", ".unity", ".meta", ".md", ".xml", ".asmdef", ".json"
        };
        
        public static readonly HashSet<Type> ExcludeAssetType = new HashSet<Type>
        {
            typeof(AssetBundlePackageBuildSettings),
            typeof(BundlePathCatalog),
        };

        [MenuItem("Build/Internal/Update Path Catalog")]
        public static void BuildWholeProjectRefeferences()
        {
            BuildOrUpdate("Assets");
        }
        
        public static BundlePathCatalog BuildOrUpdate(string path)
        {
            var catalogFilePath = Path.Combine(path, CatalogName);
            var catalogAsset = AssetDatabase.LoadAssetAtPath<BundlePathCatalog>(catalogFilePath);
            bool assetFileExists = catalogAsset != null;
            if (catalogAsset == null)
            {
                catalogAsset = CreateInstance<BundlePathCatalog>();
            }
            
            var allFiles = System.IO.Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            List<Entry> result = new List<Entry>();
            foreach (var file in allFiles)
            {
                var extension = Path.GetExtension(file);
                if (ExcludeExtensions.Contains(extension)) continue;
                
                var mainAsset = AssetDatabase.LoadMainAssetAtPath(file);
                if (mainAsset == null) continue;
                if (ExcludeAssetType.Contains(mainAsset.GetType())) continue;
                
                var assets = AssetDatabase.LoadAllAssetsAtPath(file);
                foreach (var asset in assets)
                {
                    if (ExcludeAssetType.Contains(asset.GetType())) continue;
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long localId))
                    {
                        result.Add(new Entry
                        {
                            path = file,
                            isMainAsset = mainAsset == asset,
                            subAssetName = asset.name,
                            guid = guid,
                            localId = localId,
                        });
                    }
                }
            }

            if (result.Count == 0)
            {
                if (assetFileExists)
                {
                    AssetDatabase.DeleteAsset(catalogFilePath);
                    AssetDatabase.SaveAssets();
                }
                // dont create if 0 size.
                return null;
            }
            
            catalogAsset.entries = result.ToArray();
            EditorUtility.SetDirty(catalogAsset);
            if (assetFileExists == false)
            {
                AssetDatabase.CreateAsset(catalogAsset, catalogFilePath);
            }
            AssetDatabase.SaveAssets();
            
            return catalogAsset;
        }
#endif
    }
}