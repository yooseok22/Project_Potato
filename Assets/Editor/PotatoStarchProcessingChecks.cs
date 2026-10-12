using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoStarchProcessingChecks
{
    private const string SetupRootName = "PotatoStarchTestSetup";

    [MenuItem("Tools/Project Potato/Setup Potato Starch Test")]
    public static void SetupTestScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode before setting up the test scene.");
        var existing = GameObject.Find(SetupRootName);
        if (existing)
        {
            Selection.activeGameObject = existing;
            EditorGUIUtility.PingObject(existing);
            Debug.Log("[PotatoStarch] 테스트 설비가 이미 현재 씬에 있습니다.", existing);
            return;
        }

        var player = UnityEngine.Object.FindAnyObjectByType<PlayerInteractionController>();
        if (!player) throw new InvalidOperationException("The active scene needs a PlayerInteractionController.");
        var worldClock = UnityEngine.Object.FindAnyObjectByType<WorldClock>();
        if (!worldClock) throw new InvalidOperationException("The active scene needs a WorldClock.");

        Transform playerTransform = player.transform;
        Vector3 forward = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        var character = player.GetComponent<CharacterController>();
        float floorY = character ? character.bounds.min.y : playerTransform.position.y - 1f;
        Vector3 stationCenter = playerTransform.position + forward * 2.2f;
        stationCenter.y = floorY;

        var root = new GameObject(SetupRootName);
        Undo.RegisterCreatedObjectUndo(root, "Create potato starch test setup");
        var processor = root.AddComponent<PotatoStarchProcessor>();

        var blenderDock = CreatePrimitive("StarchBlender_Test", PrimitiveType.Cylinder, root.transform,
            stationCenter - right * 0.8f + Vector3.up * 0.55f, new Vector3(0.45f, 0.55f, 0.45f));
        var blender = blenderDock.AddComponent<StarchBlender>();
        var blenderContainer = CreatePrimitive("BlenderContainer_Carry", PrimitiveType.Cylinder,
            blenderDock.transform, Vector3.up * 0.75f, new Vector3(0.6f, 0.35f, 0.6f), true);
        var blenderCarry = blenderContainer.AddComponent<ProcessingCarryItem>();

        var sieveObject = CreatePrimitive("StarchSieve_Test", PrimitiveType.Cube, root.transform,
            stationCenter + right * 0.8f + Vector3.up * 0.7f, new Vector3(1f, 1.4f, 1f));
        var sieve = sieveObject.AddComponent<StarchSieve>();
        var trayObject = CreatePrimitive("DryingTray_Test", PrimitiveType.Cube, sieveObject.transform,
            new Vector3(0f, -0.58f, 0.72f), new Vector3(0.9f, 0.12f, 0.65f), true);
        var trayCarry = trayObject.AddComponent<ProcessingCarryItem>();
        var tray = trayObject.AddComponent<StarchDryingTray>();

        var outdoorGround = CreatePrimitive("OutdoorDryingGround_Test", PrimitiveType.Plane, root.transform,
            stationCenter + forward * 4f + Vector3.up * 0.01f, new Vector3(0.45f, 1f, 0.45f));
        var outdoor = outdoorGround.AddComponent<OutdoorDryingSurface>();

        processor.Configure(worldClock, blender, sieve, tray);
        blender.Configure(processor);
        sieve.Configure(processor);
        tray.Configure(processor);
        outdoor.Configure(processor);

        EditorUtility.SetDirty(processor);
        EditorUtility.SetDirty(blender);
        EditorUtility.SetDirty(sieve);
        EditorUtility.SetDirty(tray);
        EditorUtility.SetDirty(outdoor);
        EditorUtility.SetDirty(blenderCarry);
        EditorUtility.SetDirty(trayCarry);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log("[PotatoStarch] 테스트 설비 배치 완료. Play 후 믹서기부터 [E]로 상호작용하세요.", root);
    }

    [MenuItem("Tools/Project Potato/Validate Potato Starch Processing")]
    public static void RunValidation()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Potato starch processing check failed: " + message);
            checks++;
        }

        try
        {
            var root = new GameObject("PotatoStarchProcessingChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var clock = root.AddComponent<WorldClock>();
            var processor = root.AddComponent<PotatoStarchProcessor>();

            var blenderObject = new GameObject("Blender");
            blenderObject.transform.SetParent(root.transform);
            var blender = blenderObject.AddComponent<StarchBlender>();
            var blenderCarry = blenderObject.AddComponent<ProcessingCarryItem>();

            var sieveObject = new GameObject("Sieve");
            sieveObject.transform.SetParent(root.transform);
            var sieve = sieveObject.AddComponent<StarchSieve>();

            var trayObject = new GameObject("Tray");
            trayObject.transform.SetParent(root.transform);
            var trayCarry = trayObject.AddComponent<ProcessingCarryItem>();
            var tray = trayObject.AddComponent<StarchDryingTray>();

            processor.Configure(clock, blender, sieve, tray);
            blender.Configure(processor);
            sieve.Configure(processor);
            tray.Configure(processor);

            Check(processor.BlenderState == StarchBlenderState.DockedEmpty &&
                  processor.SieveState == StarchSieveState.Filling &&
                  processor.TrayState == StarchTrayState.InstalledEmpty,
                "initial states");
            Check(processor.TryStartGrinding(), "first grind starts without inventory");
            Check(!processor.TryStartGrinding(), "duplicate grind is blocked");
            clock.AdvanceBy(4.9d);
            Check(processor.BlenderState == StarchBlenderState.Grinding,
                "grinding remains active before five seconds");
            Check(!processor.TryTakeBlender(null), "blender cannot be taken while grinding");
            clock.AdvanceBy(0.1d);
            Check(processor.BlenderState == StarchBlenderState.GroundReady,
                "grinding completes at five seconds");

            for (int batch = 1; batch <= 5; batch++)
            {
                Check(processor.TryTakeBlender(null), "batch " + batch + " blender pickup");
                Check(processor.TryPourBatch(null), "batch " + batch + " pour");
                Check(processor.SieveFillCount == batch, "batch " + batch + " fill count");
                Check(!processor.TryPourBatch(null), "batch " + batch + " cannot pour twice");
                Check(processor.TryReturnBlender(null), "batch " + batch + " blender return");
                if (batch == 5) break;
                Check(processor.TryStartGrinding(), "batch " + (batch + 1) + " grind starts");
                clock.AdvanceBy(5d);
                Check(processor.BlenderState == StarchBlenderState.GroundReady,
                    "batch " + (batch + 1) + " grind completes");
            }

            Check(processor.SieveState == StarchSieveState.Draining && processor.SieveFillCount == 5,
                "fifth batch starts draining and capacity never exceeds five");
            Check(!processor.TryTakeWetTray(null), "tray cannot be taken before draining completes");
            clock.AdvanceBy(4.9d);
            Check(processor.SieveState == StarchSieveState.Draining,
                "draining remains active before five seconds");
            clock.AdvanceBy(0.1d);
            Check(processor.SieveState == StarchSieveState.WetStarchReady &&
                  processor.TrayState == StarchTrayState.InstalledWet,
                "wet starch becomes ready at five seconds");
            Check(sieve.Prompt == "[E] 건조 틀 꺼내기",
                "completed sieve exposes the tray pickup prompt");
            sieve.Interact(null);
            Check(processor.TrayState == StarchTrayState.CarriedWet,
                "completed sieve interaction routes to wet tray pickup");
            Check(!processor.TryPlaceTray(null, Vector3.zero, Quaternion.identity, false),
                "indoor placement is blocked");
            Check(processor.TryPlaceTray(null, Vector3.zero, Quaternion.identity, true),
                "outdoor placement starts drying");
            clock.AdvanceBy(149.9d);
            Check(processor.TrayState == StarchTrayState.Drying,
                "drying remains active before 150 seconds");
            Check(!processor.TryCollectProduct(), "product cannot be collected while drying");
            clock.AdvanceBy(0.1d);
            Check(processor.TrayState == StarchTrayState.Dried, "drying completes at 150 seconds");
            Check(processor.TryCollectProduct() && processor.LastProducedPackCount == 5 &&
                  processor.TotalProducedPackCount == 5,
                "one cycle produces five test packs");
            Check(processor.SieveFillCount == 0 && processor.TrayState == StarchTrayState.InstalledEmpty,
                "collection resets the next cycle");
            Check(!processor.TryCollectProduct() && processor.TotalProducedPackCount == 5,
                "product cannot be collected twice");

            Debug.Log($"[PotatoStarchProcessingChecks] PASS: {checks} checks.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent,
        Vector3 position, Vector3 scale, bool local = false)
    {
        var value = GameObject.CreatePrimitive(type);
        value.name = name;
        value.transform.SetParent(parent, false);
        if (local) value.transform.localPosition = position;
        else value.transform.position = position;
        value.transform.localRotation = Quaternion.identity;
        value.transform.localScale = scale;
        Undo.RegisterCreatedObjectUndo(value, "Create " + name);
        return value;
    }
}
