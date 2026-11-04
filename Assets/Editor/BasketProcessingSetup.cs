using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BasketProcessingSetup
{
    [MenuItem("Tools/Project Potato/Connect Processing Basket")]
    public static void Connect()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        const string path = "Assets/Prefabs/Player.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!prefab.GetComponent<BasketProcessingController>()) prefab.AddComponent<BasketProcessingController>();
            PrefabUtility.SaveAsPrefabAsset(prefab, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var player in UnityEngine.Object.FindObjectsByType<PlayerBasketInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!player.GetComponent<BasketProcessingController>()) Undo.AddComponent<BasketProcessingController>(player.gameObject);
            EditorUtility.SetDirty(player.gameObject);
        }
        foreach (var processor in UnityEngine.Object.FindObjectsByType<PotatoStarchProcessor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!processor.GetComponent<SaveIdentity>()) Undo.AddComponent<SaveIdentity>(processor.gameObject);
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Processing Basket] Connected player prototype basket, cursor selection and processor save identities.");
    }
}
