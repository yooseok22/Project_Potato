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
            var c = new Vector2Int(5, 6);
            var d = new Vector2Int(7, 8);
            int objectsBefore = scene.GetRootGameObjects().Length;
            int notifications = 0;
            system.FarmingStageChanged += (g, cell, stage) =>
            {
                Check(g == ground && (cell == a || cell == b || cell == c), "event preserves grid and coordinates");
                notifications++;
            };
            Check(!system.TryStartFarming(null, a, clock), "missing grid rejected");
            Check(!system.TryStartFarming(ground, a, null), "missing clock rejected");
            Check(!system.TryStartFarming(ground, new Vector2Int(-1, 0), clock), "invalid cell rejected");
            ground.TrySetCellState(a, GroundCellState.PlantingHole);
            Check(system.TryStartFarming(ground, a, clock), "planting hole can register");
            Check(ground.GetCellState(a) == GroundCellState.PotatoCovered, "planting switches to G_002");
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
            Check(system.TryDamage(ground, b, 30f), "harvest test crop takes damage");
            Check(system.TryCompleteHarvest(ground, b, out float harvestedHp) && harvestedHp == 70f &&
                !system.TryGetFarmingTile(ground, b, out _),
                "complete harvest returns current HP and removes farming data");
            Check(!system.TryCompleteHarvest(ground, b, out float missingHp) && missingHp == 0f,
                "completed crop cannot be harvested twice");

            int healthNotifications = 0;
            int deathNotifications = 0;
            system.FarmingHealthChanged += (g, cell, damage, health) =>
            {
                Check(g == ground && cell == c && damage > 0f && health >= 0f,
                    "health event preserves grid, coordinates, damage and remaining health");
                healthNotifications++;
            };
            system.FarmingTileDied += (g, cell) =>
            {
                Check(g == ground && cell == c, "death event preserves grid and coordinates");
                deathNotifications++;
            };
            ground.TrySetCellState(c, GroundCellState.PlantingHole);
            Check(system.TryStartFarming(ground, c, clock), "damage test crop registered");
            Check(system.TryGetFarmingTile(ground, c, out var damagedCrop), "damage test crop lookup");
            Check(damagedCrop is IDamageable && damagedCrop.MaxHealth == 100f &&
                damagedCrop.CurrentHealth == 100f && damagedCrop.returnPotatoHp() == 100f &&
                !damagedCrop.IsDead, "crop starts at full default health");
            Check(system.TryWater(ground, c), "damage test crop starts growing");
            Check(!system.TryDamage(ground, new Vector2Int(20, 20), 10f) &&
                !system.TryDamage(ground, c, 0f) && !system.TryDamage(ground, c, float.NaN),
                "invalid damage does not change health");
            Check(system.TryDamage(ground, c, 25f) && damagedCrop.CurrentHealth == 75f,
                "coordinate damage reduces health");
            Check(damagedCrop.returnPotatoHp() == 75f, "harvest API returns current crop health");
            Check(system.TryDamage(ground, c, 25f) && damagedCrop.CurrentHealth == 50f,
                "repeated coordinate damage reduces health");
            Check(system.TryDamage(ground, c, 100f) && damagedCrop.CurrentHealth == 0f && damagedCrop.IsDead,
                "lethal damage clamps health to zero");
            Check(healthNotifications == 3 && deathNotifications == 1 &&
                !system.TryDamage(ground, c, 1f), "dead crop ignores further damage");
            clock.AdvanceBy(300);
            Check(damagedCrop.Stage == PotatoGrowthStage.SoilMound,
                "dead crop remains registered but stops growing");

            ground.TrySetCellState(d, GroundCellState.SproutSecond);
            Check(ground.CanCoverPotato(d) && ground.TryCoverPotato(d)
                && ground.GetCellState(d) == GroundCellState.SoilCoveredFirst,
                "GB_001 can be covered and becomes GN_001");
            ground.TrySetCellState(d, GroundCellState.LateHarvest);
            Check(!ground.CanCoverPotato(d) && !ground.TryCoverPotato(d),
                "GB_005 cannot be covered");

            ground.TrySetCellState(d, GroundCellState.SoilCoveredThird);
            Check(ground.CanUseSickle(d) && ground.TryUseSickle(d)
                && ground.GetCellState(d) == GroundCellState.CoveredCut,
                "sickle changes GN_003 through GN_005 to GN F");
            Check(ground.TryUsePotatoFork(d), "potato fork advances GN F");
            GroundCellState forkState = ground.GetCellState(d);
            Check(forkState == GroundCellState.CoveredHighYield
                || forkState == GroundCellState.CoveredHighHidden,
                "first fork use selects HY or HN");

            if (forkState == GroundCellState.CoveredHighYield)
            {
                Check(ground.TryUsePotatoFork(d)
                    && ground.GetCellState(d) == GroundCellState.CoveredMidEarly,
                    "HY advances to ME");
                Check(ground.TryHarvestPotato(d, out var quality, out string code)
                    && quality == PotatoHarvestQuality.Damaged && code == "ME",
                    "ME harvest reports a damaged potato");
            }
            else
            {
                Check(ground.TryUsePotatoFork(d), "HN advances to MY or MN");
                GroundCellState middleState = ground.GetCellState(d);
                Check(middleState == GroundCellState.CoveredMidYield
                    || middleState == GroundCellState.CoveredMidHidden,
                    "HN selects MY or MN");
                Check(ground.TryUsePotatoFork(d), "MY or MN advances to a final harvest state");
                GroundCellState finalState = ground.GetCellState(d);
                bool normal = finalState == GroundCellState.CoveredLowYield;
                Check(finalState == GroundCellState.CoveredLowEarly || normal,
                    "MY becomes LE and MN becomes LY");
                Check(ground.TryHarvestPotato(d, out var quality, out string code)
                    && quality == (normal ? PotatoHarvestQuality.Normal : PotatoHarvestQuality.Damaged)
                    && code == (normal ? "LY" : "LE"),
                    "LE and LY report the correct potato quality");
            }
            Check(ground.GetCellState(d) == GroundCellState.PlantingHole,
                "harvest returns the tile to G_001");

            GroundCellState[] forkCompletionStates =
            {
                GroundCellState.UncoveredMidEarly,
                GroundCellState.UncoveredLowEarly,
                GroundCellState.UncoveredLowYield,
                GroundCellState.CoveredMidEarly,
                GroundCellState.CoveredLowEarly,
                GroundCellState.CoveredLowYield
            };
            foreach (GroundCellState completionState in forkCompletionStates)
            {
                ground.TrySetCellState(d, completionState);
                Check(ground.CanUsePotatoFork(d) && ground.TryUsePotatoFork(d)
                    && ground.GetCellState(d) == GroundCellState.PlantingHole,
                    $"potato fork changes {completionState} to G_001");
            }
            Debug.Log($"[PotatoFarmingChecks] PASS: {checks} checks.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}

