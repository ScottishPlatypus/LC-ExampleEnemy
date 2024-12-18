using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildMultipleCosmeticsIntoBundle
{
    [MenuItem("Assets/Build Selected Cosmetics Into Bundle")]
    static void BuildSelected()
    {
        Object[] selection = Selection.objects;
        string name = selection[0].name;
        string assetBundleDirectory = "Assets/BuiltCosmetics";
        if (!Directory.Exists(assetBundleDirectory)){
            Directory.CreateDirectory(assetBundleDirectory);
        }
        
        BuildPipeline.BuildAssetBundle(Selection.activeObject, selection, assetBundleDirectory+"/default.cosmetics", BuildAssetBundleOptions.CompleteAssets | BuildAssetBundleOptions.CollectDependencies, BuildTarget.StandaloneWindows64);
        Debug.Log("Built asset bundle with "+selection.Length+" assets.");
        
        // Open the folder where the bundle is located
        EditorUtility.RevealInFinder(assetBundleDirectory+"/default.cosmetics");
    }
}
