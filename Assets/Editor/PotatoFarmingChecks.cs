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
            typeof(GroundGrid).GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)?.Invoke(ground, null);
            var clock = root.AddComponent<WorldClock>();
            var system = root.AddComponent<PotatoFarmingSystem>();
            var a = new Vector2Int(1, 2);
            var b = new Vector2Int(3, 4);
            var visualCell = new Vector2Int(10, 10);
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
            Check(ground.TryUseHoeSecondary(visualCell) &&
                ground.GetCellState(visualCell) == GroundCellState.FieldSoil, "first hoe use digs field soil");
            Check(ground.TryUseHoeSecondary(visualCell) &&
                ground.GetCellState(visualCell) == GroundCellState.PlantingHole, "second hoe use creates tile 016");
            for (int value = (int)GroundCellState.PlantingHole; value <= (int)GroundCellState.BoarDugField; value++)
            {
                var state = (GroundCellState)value;
                ground.TrySetCellState(visualCell, state);
                Check(TileVisualResolver.Resolve(ground, visualCell).Kind == 15 + (int)state - (int)GroundCellState.PlantingHole,
                    state + " maps to its authored tile");
            }
            ground.TrySetCellState(a, GroundCellState.PlantingHole);
            Check(system.TryStartFarming(ground, a, clock), "planting hole can register");
            Check(ground.GetCellState(a) == GroundCellState.PotatoCovered, "planting switches to tile 017");
            Check(!system.TryStartFarming(ground, a, clock), "duplicate does not reset age");
            Check(system.TryGetFarmingTile(ground, a, out var first), "lookup by coordinates");
            clock.AdvanceBy(600);
            Check(!first.IsWatered && first.GrowthStartedAt == null && first.AgeSeconds == 0 &&
                first.Progress == 0 && !first.IsReadyToHarvest &&
                ground.GetCellState(a) == GroundCellState.PotatoCovered && notifications == 1,
                "unwatered crop does not grow during a long wait");
            Check(!system.TryWater(ground, b), "cannot water unregistered tile");
            Check(!system.TryWater(null, a), "cannot water missing grid");
            Check(!system.TryWater(ground, new Vector2Int(-1, 0)), "cannot water invalid coordinates");
            Check(system.TryWater(ground, a) && first.GrowthStartedAt == clock.ElapsedSeconds,
                "first watering records growth start");
            clock.AdvanceBy(20);
            Check(!system.TryWater(ground, a) && first.AgeSeconds == 20, "repeat watering does not reset growth");
            ground.TrySetCellState(b, GroundCellState.PlantingHole);
            Check(system.TryStartFarming(ground, b, clock), "second planting");
            Check(ground.GetCellState(b) == GroundCellState.PotatoCovered, "initial state forwarded");
            system.TryGetFarmingTile(ground, b, out var second);
            Check(system.TryWater(ground, b), "water second tile independently");
            clock.AdvanceBy(20);
            system.RefreshGrowth();
            system.RefreshGrowth();
            Check(first.Stage == PotatoGrowthStage.Sprout && second.Stage == PotatoGrowthStage.SoilMound,
                "staggered planting grows independently");
            Check(system.GetFarmingTiles().Length == 2 && notifications == 3, "registered tiles and stage notifications");
            Check(ground.GetCellState(a) == GroundCellState.SproutFirst, "40 seconds forwards SproutFirst");
            clock.AdvanceBy(20);
            Check(ground.GetCellState(b) == GroundCellState.SproutFirst, "second tile forwards independently");
            Check(system.StopFarming(ground, a) && !system.StopFarming(ground, a), "unregister is single-use");
            Check(system.FarmingCount == 1 && !system.TryGetFarmingTile(ground, a, out _), "unregister removes coordinate");
            ground.TrySetCellState(a, GroundCellState.PlantingHole);
            Check(system.TryStartFarming(ground, a, clock), "removed coordinate can register again");
            system.TryGetFarmingTile(ground, a, out var replanted);
            Check(replanted.AgeSeconds == 0, "replant starts new timer");
            Check(!replanted.IsWatered, "replant requires fresh watering");
            Check(system.TryWater(ground, a), "water replanted tile");
            Check(root.transform.childCount == 0 && scene.GetRootGameObjects().Length == objectsBefore,
                "planting creates no GameObjects");
            clock.IsPaused = true;
            clock.Tick(50);
            Check(replanted.AgeSeconds == 0, "central farming respects pause");
            clock.IsPaused = false;
            clock.Speed = 2;
            clock.Tick(20);
            Check(replanted.Stage == PotatoGrowthStage.Sprout, "central clock subscription and speed");
            double[] boundaries = { 80, 150, 220, 260, 300 };
            GroundCellState[] expected = { GroundCellState.SproutSecond, GroundCellState.SproutThird,
                GroundCellState.EarlyHarvest, GroundCellState.ReadyToHarvest, GroundCellState.LateHarvest };
            for (int i = 0; i < boundaries.Length; i++)
            {
                clock.AdvanceBy(boundaries[i] - replanted.AgeSeconds - 0.001);
                Check(ground.GetCellState(a) != expected[i], "before boundary " + boundaries[i]);
                if (boundaries[i] == 260) Check(!replanted.IsReadyToHarvest, "not ready before 260 seconds");
                clock.AdvanceBy(0.0011);
                Check(ground.GetCellState(a) == expected[i], "state at boundary " + boundaries[i]);
                if (boundaries[i] >= 260) Check(replanted.IsReadyToHarvest, "ready from 260 seconds onward");
            }
            int atMaturity = notifications;
            clock.AdvanceBy(600);
            Check(notifications == atMaturity, "mature tiles do not emit repeated growth events");
            system.StopFarming(ground, a);
            ground.TrySetCellState(a, GroundCellState.PlantingHole);
            system.TryStartFarming(ground, a, clock);
            system.TryWater(ground, a);
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

