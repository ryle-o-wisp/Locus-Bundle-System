using System;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.IO;
using System.Net;
using BundleSystem;

public class AssetBundleBuildWindow : EditorWindow 
{
    [MenuItem ("Build/AssetBundle Builder")]

    public static void ShowWindow () {
        EditorWindow.GetWindow(typeof(AssetBundleBuildWindow));
    }

    void OnGUI ()
    {
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("Analysis", GUILayout.Width(100)))
        {
            var menu = new GenericMenu();

            const string analyzeCrossReferencedAssetsMenuName = "Report cross referenced items between Settings";
            if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings == null)
            {
                menu.AddDisabledItem(new GUIContent($"{analyzeCrossReferencedAssetsMenuName} (no global settings specified)"));
            } 
            else if(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.disallowCrossReference) 
            {
                menu.AddItem(new GUIContent(analyzeCrossReferencedAssetsMenuName), false, () =>
                {
                    var report = AssetsCrossReferenceValidator.CreateReport(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings);
                    var reportPath = Path.Combine(FileUtil.GetUniqueTempPathInProject(), "cross-referenced-report.txt");
                    var tempPath = Path.GetDirectoryName(reportPath);

                    if (string.IsNullOrWhiteSpace(tempPath) == false && Directory.Exists(tempPath) == false)
                    {
                        Directory.CreateDirectory(tempPath);
                    } 
                
                    File.WriteAllText(reportPath, report);
                    EditorUtility.RevealInFinder(reportPath);
                    
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent($"{analyzeCrossReferencedAssetsMenuName} (option disabled in global settings)"));
            }

            const string analyzeSharedBundlesMenuName = "Report Shared Items between Bundles";
            if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings == null)
            {
                menu.AddDisabledItem(new GUIContent($"{analyzeSharedBundlesMenuName} (no global settings specified)"));
            }
            else
            {
                menu.AddItem(new GUIContent(analyzeSharedBundlesMenuName), false, ()=>
                {
                    if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings != null)
                    {
                        AssetbundleBuilder.WriteExpectedSharedBundles(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetActiveSettingEntries());
                    }
                    else
                    {
                        Debug.LogError($@"{nameof(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings)} is missing");
                    }
                });
            }
            
            
            const string analyzeUniquePackageName = "Report package names are unique";
            if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings == null)
            {
                menu.AddDisabledItem(new GUIContent($"{analyzeUniquePackageName} (no global settings specified)"));
            }
            else
            {
                menu.AddItem(new GUIContent(analyzeUniquePackageName), false, ()=>
                {
                    var report = UniquePackageNameValidator.CreateReport(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings);
                    var reportPath = Path.Combine(FileUtil.GetUniqueTempPathInProject(), "unique-package-names-report.txt");
                    var tempPath = Path.GetDirectoryName(reportPath);

                    if (string.IsNullOrWhiteSpace(tempPath) == false && Directory.Exists(tempPath) == false)
                    {
                        Directory.CreateDirectory(tempPath);
                    } 
                
                    File.WriteAllText(reportPath, report);
                    EditorUtility.RevealInFinder(reportPath);
                });
            }
            
            menu.ShowAsContext();
        }
        
        if (GUILayout.Button("Build", GUILayout.Width(100)))
        {
            var menu = new GenericMenu();
            foreach (var buildOption in new[]
                 {
                     (MenuName: "Android", MenuAction: (GenericMenu.MenuFunction)delegate()
                     {
                         AssetbundleBuilder.BuildAssetBundles(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings, PlatformType.Android);
                     }),
                     (MenuName: "iOS", MenuAction: (GenericMenu.MenuFunction)delegate()
                     {
                         AssetbundleBuilder.BuildAssetBundles(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings, PlatformType.IOS);
                     })
                 })
            {
                if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings == null)
                {
                    menu.AddDisabledItem(new GUIContent($"{buildOption.MenuName} (global settings is not specified)"));
                }
                else
                {
                    menu.AddItem(new GUIContent(buildOption.MenuName), false, buildOption.MenuAction);
                }
            }
            menu.ShowAsContext();
        }
        
        if (GUILayout.Button("Clean", GUILayout.Width(100)))
        {
            var menu = new GenericMenu();
            
            foreach (var cleanOption in new[]
                 {
                     (MenuName: "Android", MenuAction: (GenericMenu.MenuFunction)delegate()
                     {
                         AssetbundleBuilder.CleanBuiltAssetBundles(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetActiveSettingEntries(), AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetDistributionProfileByPlatform(PlatformType.Android), BuildType.Local, BuildTarget.Android);
                         AssetbundleBuilder.CleanBuiltAssetBundles(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetActiveSettingEntries(), AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetDistributionProfileByPlatform(PlatformType.Android), BuildType.Remote, BuildTarget.Android);
                         AssetDatabase.Refresh();
                     }),
                     (MenuName: "iOS", MenuAction: (GenericMenu.MenuFunction)delegate()
                     {
                         AssetbundleBuilder.CleanBuiltAssetBundles(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetActiveSettingEntries(), AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetDistributionProfileByPlatform(PlatformType.IOS), BuildType.Local, BuildTarget.iOS);
                         AssetbundleBuilder.CleanBuiltAssetBundles(AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetActiveSettingEntries(), AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings.GetDistributionProfileByPlatform(PlatformType.IOS), BuildType.Remote, BuildTarget.iOS);
                         AssetDatabase.Refresh();
                     })
                 })
            {
                if (AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings == null)
                {
                    menu.AddDisabledItem(new GUIContent($"{cleanOption.MenuName} (global settings is not specified)"));
                }
                else
                {
                    menu.AddItem(new GUIContent(cleanOption.MenuName), false, cleanOption.MenuAction);
                }
            }
            menu.ShowAsContext();
        }
        
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(10);
        
        var globalSettings = EditorGUILayout.ObjectField("Global Settings", AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings, typeof(AssetBundleBuildGlobalSettings), false) as AssetBundleBuildGlobalSettings;
        if (globalSettings != AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings)
        {
            AssetBundleEditorPrefs.AssetBundleBuildGlobalSettings = globalSettings;
        }

        AssetBundleEditorPrefs.EmulateInEditor = EditorGUILayout.Toggle(nameof(AssetBundleEditorPrefs.EmulateInEditor), AssetBundleEditorPrefs.EmulateInEditor);
        AssetBundleEditorPrefs.EmulateWithoutRemoteURL = EditorGUILayout.Toggle(nameof(AssetBundleEditorPrefs.EmulateWithoutRemoteURL), AssetBundleEditorPrefs.EmulateWithoutRemoteURL);
        AssetBundleEditorPrefs.CleanCacheInEditor = EditorGUILayout.Toggle(nameof(AssetBundleEditorPrefs.CleanCacheInEditor), AssetBundleEditorPrefs.CleanCacheInEditor);
        AssetBundleEditorPrefs.IncrementalBuild = EditorGUILayout.Toggle(nameof(AssetBundleEditorPrefs.IncrementalBuild), AssetBundleEditorPrefs.IncrementalBuild);
    }
}
