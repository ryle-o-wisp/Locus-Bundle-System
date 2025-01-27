﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BundleSystem
{
    /// <summary>
    /// this class finds out duplicated topmost assets
    /// and make them into one single shared bundle one by one(to reduce bundle rebuild)
    /// so that there would be no asset duplicated
    /// </summary>
    public static class AssetDependencyTree
    {
        public class ProcessResults
        {
            public Dictionary<string, ProcessResult> ResultsBySettingPath = new Dictionary<string, ProcessResult>();
        }

        public static ProcessResults ProcessDependencyTree(AssetBundlePackageBuildSettings[] allSettings)
        {
            var results = new ProcessResults();
            foreach (var setting in allSettings)
            {
                var settingPath = AssetDatabase.GetAssetPath(setting);
                if (string.IsNullOrEmpty(settingPath))
                {
                    continue;
                }
                
                var bundles = AssetbundleBuilder.GetAssetBundlesList(setting);
                var result = ProcessDependencyTree(bundles);
                results.ResultsBySettingPath[settingPath] = result;
            }
            return results;
        }
        
        public class ProcessResult
        {
            public Dictionary<string, HashSet<string>> BundleDependencies;
            public List<AssetBundleBuild> SharedBundles;
            public string[] Assets;
        }

        public static ProcessResult ProcessDependencyTree(List<AssetBundleBuild> definedBundles)
        {
            var context = new Context();
            var rootNodesToProcess = new List<RootNode>();

            //collecting reference should be done after adding all root nodes
            //if not, there might be false positive shared bundle that already exist in bundle defines
            foreach(var bundle in definedBundles)
            {
                var depsHash = new HashSet<string>();
                context.DependencyDic.Add(bundle.assetBundleName, depsHash);
                foreach(var asset in bundle.assetNames)
                {
                    var rootNode = new RootNode(asset, bundle.assetBundleName, depsHash, false);
                    try
                    {
                        context.RootNodes.Add(asset, rootNode);
                        rootNodesToProcess.Add(rootNode);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        Debug.LogError($@"Errored on building tree: {asset} is duplicated.");
                        throw;
                    }
                }
            }

            //actually analize and create shared bundles
            foreach (var node in rootNodesToProcess) node.CollectNodes(context);

            var resultList = new List<AssetBundleBuild>();
            //convert found shared node proper struct
            foreach(var sharedRootNode in context.ResultSharedNodes)
            {
                var assetNames = new string[] { sharedRootNode.Path };
                var bundleDefinition = new AssetBundleBuild()
                {
                    assetBundleName = Utility.GetBundleNameWithExtension(sharedRootNode.BundleName),
                    assetNames = assetNames,
                    addressableNames = assetNames
                };
                resultList.Add(bundleDefinition);
            }

            string[] GetAllReferences()
            {
                var nodes = new List<Node>();
                var visit = new HashSet<string>();
                var q = new Queue<Node>();
                foreach (var node in context.RootNodes.Values)
                {
                    q.Enqueue(node);
                }

                while (q.TryDequeue(out var seek))
                {
                    if (visit.Add(seek.Path))
                    {
                        nodes.Add(seek);
                        foreach (var n in seek.Children.Values)
                        {
                            q.Enqueue(n);
                        }
                    }
                }

                return nodes.Select(n=>n.Path).ToArray();
            }

            return new ProcessResult() { BundleDependencies = context.DependencyDic, SharedBundles = resultList, Assets = GetAllReferences() };
        }

        //actual node tree context
        public class Context
        {
            public Dictionary<string, HashSet<string>> DependencyDic = new Dictionary<string, HashSet<string>>();
            public Dictionary<string, RootNode> RootNodes = new Dictionary<string, RootNode>();
            public Dictionary<string, Node> IndirectNodes = new Dictionary<string, Node>();
            public List<RootNode> ResultSharedNodes = new List<RootNode>();
        }

        public class RootNode : Node
        {
            public string BundleName { get; private set; }
            public bool IsShared { get; private set; }
            public HashSet<string> ReferencedBundleNames;

            public RootNode(string path, string bundleName, HashSet<string> deps, bool isShared) : base(path, null)
            {
                IsShared = isShared;
                BundleName = bundleName;
                Root = this;
                ReferencedBundleNames = deps;
            }
        }

        public class Node
        {
            public RootNode Root { get; protected set; }
            public string Path { get; private set; }
            public Dictionary<string, Node> Children = new Dictionary<string, Node>();
            public bool IsRoot => Root == this;
            public bool HasChild => Children.Count > 0;

            public Node(string path, RootNode root)
            {
                Root = root;
                Path = path;
            }

            public void RemoveFromTree(Context context)
            {
                context.IndirectNodes.Remove(Path);
                foreach (var kv in Children) kv.Value.RemoveFromTree(context);
            }

            public void CollectNodes(Context context)
            {
                var childDeps = AssetDatabase.GetDependencies(Path, false);

                //if it's a scene unwarp placed prefab directly into the scene
                if(Path.EndsWith(".unity")) childDeps = Utility.UnwarpSceneEncodedPrefabs(Path, childDeps);

                foreach (var child in childDeps)
                {
                    //is not bundled file
                    if (!Utility.IsAssetCanBundled(child)) continue;

                    //already root node, wont be included multiple times
                    if (context.RootNodes.TryGetValue(child, out var rootNode))
                    {
                        Root.ReferencedBundleNames.Add(rootNode.Root.BundleName);
                        continue;
                    }

                    //check if it's already indirect node (skip if it's same bundle)
                    //circular dependency will be blocked by indirect check
                    if (context.IndirectNodes.TryGetValue(child, out var node))
                    {
                        if (node.Root.BundleName != Root.BundleName)
                        {
                            node.RemoveFromTree(context);

                            var newName = $"Shared_{AssetDatabase.AssetPathToGUID(child)}.bundle";
                            var depsHash = new HashSet<string>();
                            context.DependencyDic.Add(newName, depsHash);
                            var newRoot = new RootNode(child, newName, depsHash, true);

                            //add deps
                            node.Root.ReferencedBundleNames.Add(newName);
                            Root.ReferencedBundleNames.Add(newName);

                            context.RootNodes.Add(child, newRoot);
                            context.ResultSharedNodes.Add(newRoot);
                            //is it okay to do here?
                            newRoot.CollectNodes(context);
                        }
                        continue;
                    }

                    //if not, add to indirect node
                    var childNode = new Node(child, Root);
                    context.IndirectNodes.Add(child, childNode);
                    Children.Add(child, childNode);
                    childNode.CollectNodes(context);
                }
            }
        }
    }
}
