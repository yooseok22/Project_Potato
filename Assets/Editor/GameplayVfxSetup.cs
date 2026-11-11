using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameplayVfxSetup
{
    private const string Folder = "Assets/Fbx/VFX/";
    private const string FarmPrefab = "Assets/Prefabs/Farming/PotatoFarmingSystem.prefab";

    private static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".prefab")
        ?? throw new InvalidOperationException("Missing VFX prefab: " + name);

    [MenuItem("Tools/Project Potato/Connect Gameplay VFX")]
    public static void ConnectScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene = SceneManager.GetActiveScene();
        var farms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PotatoFarmingSystem>(true)).ToArray();
        var processors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PotatoStarchProcessor>(true)).ToArray();
        if (farms.Length == 0) throw new InvalidOperationException("The scene needs PotatoFarmingSystem.");
        PreparePrefabs();
        foreach (var farm in farms) ConnectFarm(farm.gameObject, true);
        foreach (var processor in processors)
        {
            // The inner basket bone has the actual basket pose, without the FBX axis conversion.
            var basket = processor.GetComponentsInChildren<Transform>(true).Single(t =>
                t.name == "B_Starch Water Basket" && t.parent && t.parent.name == t.name);
            var anchor = basket.Find("StarchDripAnchor");
            if (!anchor)
            {
                var go = new GameObject("StarchDripAnchor");
                Undo.RegisterCreatedObjectUndo(go, "Create starch drip anchor");
                Undo.SetTransformParent(go.transform, basket, "Attach starch drip anchor");
                anchor = go.transform;
                anchor.localPosition = new Vector3(0f, 0.12f, 0f);
                anchor.localRotation = Quaternion.identity;
                anchor.localScale = Vector3.one;
            }
            var vfx = processor.GetComponent<StarchWaterVfxController>();
            if (!vfx) vfx = Undo.AddComponent<StarchWaterVfxController>(processor.gameObject);
            Undo.RecordObject(vfx, "Connect starch VFX");
            vfx.Configure(processor, Prefab("VFX_Starch Water Drop"), anchor);
            Dirty(vfx);
        }
        var contents = PrefabUtility.LoadPrefabContents(FarmPrefab);
        try
        {
            ConnectFarm(contents, false);
            PrefabUtility.SaveAsPrefabAsset(contents, FarmPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[GameplayVFX] Connected {farms.Length} farm(s), {processors.Length} starch station(s), and farming prefab.");
    }

    private static void ConnectFarm(GameObject go, bool undo)
    {
        var vfx = go.GetComponent<FarmingVfxController>();
        if (!vfx) vfx = undo ? Undo.AddComponent<FarmingVfxController>(go) : go.AddComponent<FarmingVfxController>();
        if (undo) Undo.RecordObject(vfx, "Connect farming VFX");
        vfx.Configure(Prefab("VFX_Boar Crash"), Prefab("VFX_Bug"), Prefab("VFX_Leaf Crash"), Prefab("VFX_Potato Crash"));
        Dirty(vfx);
    }

    private static void PreparePrefabs()
    {
        foreach (string name in new[] { "VFX_Boar Crash", "VFX_Bug", "VFX_Leaf Crash", "VFX_Potato Crash", "VFX_Starch Water Drop" })
        {
            string path = Folder + name + ".prefab";
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var system in contents.GetComponentsInChildren<ParticleSystem>(true))
                {
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = system.main;
                    main.loop = name == "VFX_Bug" || name == "VFX_Starch Water Drop";
                    main.playOnAwake = false;
                    main.stopAction = ParticleSystemStopAction.None;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }

    private static void Dirty(UnityEngine.Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
}
