using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class MinerPrefabGenerator
{
    private const string PrefabFolder = "Assets/Resources";
    private const string PrefabPath = "Assets/Resources/Miner.prefab";

    static MinerPrefabGenerator()
    {
        EditorApplication.delayCall += EnsureMinerPrefab;
    }

    private static void EnsureMinerPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;

        EnsureFolder("Assets", "Resources");
        EnsureFolder(PrefabFolder, "MinerMaterials");

        GameObject miner = MinerVisualFactory.CreateMiner();
        PrefabUtility.SaveAsPrefabAsset(miner, PrefabPath);
        Object.DestroyImmediate(miner);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created modular miner prefab at " + PrefabPath);
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }
}
