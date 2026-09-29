using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoFarmingChecks
{
    [MenuItem("Tools/Project Potato/Validate Farming Tile Integration")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Farming check failed: " + message);
            checks++;
        }
        try
        {
            var root = new GameObject("FarmingChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var ground = root.AddComponent<GroundGrid>();
            ground.SendMessage("Awake"); // Initialize runtime grid storage in the isolated Edit Mode scene.
            var clock = root.AddComponent<WorldClock>();
            var system = root.AddComponent<PotatoFarmingSystem>();
            var a = new Vector2Int(1, 2);
            var b = new Vector2Int(3, 4);
            int objectsBefore = scene.GetRootGameObjects().Length;
            int notifications = 0;
            system.FarmingStageChanged += (g, cell, stage) =>
            {
                Check(g == ground && (cell == a || cell == b), "event preserves grid and coordinates");
                notifications++;
            };
            Check(!system.TryStartFarming(null, a, clock), "missing grid rejected");
            Check(!system.TryStartFarming(ground, a, null), "missing clock rejected");
            Check(!system.TryStartFarming(ground, new Vector2Int(-1, 0), clock), "invalid cell rejected");
            ground.TrySetCellState(a, GroundCellState.PotatoCovered);
            Check(system.TryStartFarming(ground, a, clock), "already-covered tile can register");
            Check(!system.TryStartFarming(ground, a, clock), "duplicate does not reset age");
            Check(system.TryGetFarmingTile(ground, a, out var first), "lookup by coordinates");
            clock.AdvanceBy(20);
            Check(system.TryStartFarming(ground, b, clock), "second planting");
            Check(ground.GetCellState(b) == GroundCellState.PotatoCovered, "initial state forwarded");
            system.TryGetFarmingTile(ground, b, out var second);
            clock.AdvanceBy(20);
            system.RefreshGrowth();
            system.RefreshGrowth();
            Check(first.Stage == PotatoGrowthStage.Sprout && second.Stage == PotatoGrowthStage.SoilMound,
                "staggered planting grows independently");
            Check(system.GetFarmingTiles().Length == 2 && notifications == 3, "registered tiles and stage notifications");
            Check(ground.GetCellState(a) == GroundCellState.PotatoCovered, "unmapped stage preserves ground state");
            // Stand-in mapping verifies forwarding without adding states owned by the ground team.
            var settings = new SerializedObject(system);
            var mappings = settings.FindProperty("stageMappings");
            mappings.arraySize = 2;
            mappings.GetArrayElementAtIndex(1).FindPropertyRelative("stage").enumValueIndex = (int)PotatoGrowthStage.Sprout;
            mappings.GetArrayElementAtIndex(1).FindPropertyRelative("cellState").enumValueIndex = (int)GroundCellState.FieldSoil;
            settings.ApplyModifiedPropertiesWithoutUndo();
            clock.AdvanceBy(20);
            system.RefreshGrowth();
            Check(ground.GetCellState(b) == GroundCellState.FieldSoil, "configured stage uses TrySetCellState");
            Check(system.StopFarming(ground, a) && !system.StopFarming(ground, a), "unregister is single-use");
            Check(system.FarmingCount == 1 && !system.TryGetFarmingTile(ground, a, out _), "unregister removes coordinate");
            Check(system.TryStartFarming(ground, a, clock), "removed coordinate can register again");
            system.TryGetFarmingTile(ground, a, out var replanted);
            Check(replanted.AgeSeconds == 0, "replant starts new timer");
            Check(root.transform.childCount == 0 && scene.GetRootGameObjects().Length == objectsBefore,
                "planting creates no GameObjects");
            clock.IsPaused = true;
            clock.Tick(50);
            Check(replanted.AgeSeconds == 0, "central farming respects pause");
            clock.IsPaused = false;
            clock.Speed = 2;
            clock.Tick(20);
            Check(replanted.Stage == PotatoGrowthStage.Sprout, "central clock subscription and speed");
            clock.AdvanceBy(259.999);
            Check(!replanted.IsReadyToHarvest, "not ready before 300 seconds");
            clock.AdvanceBy(0.0011);
            Check(replanted.IsReadyToHarvest && replanted.Stage == PotatoGrowthStage.WitheredSixLeaves,
                "300 seconds matures crop");
            int atMaturity = notifications;
            clock.AdvanceBy(600);
            Check(notifications == atMaturity, "mature tiles do not emit repeated growth events");
            system.StopFarming(ground, a);
            system.TryStartFarming(ground, a, clock);
            system.FarmingStageChanged += (g, cell, stage) =>
            {
                if (cell == a && stage == PotatoGrowthStage.Sprout) system.StopFarming(g, cell);
            };
            clock.AdvanceBy(300);
            Check(!system.TryGetFarmingTile(ground, a, out _), "callback can remove tile during time skip");
            Debug.Log($"[PotatoFarmingChecks] PASS: {checks} checks.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}

