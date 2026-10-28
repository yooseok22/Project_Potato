using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoFarmingChecks
{
    [MenuItem("Tools/Project Potato/Validate Mulching Film")]
    public static void RunMulchingFilm()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        void Check(bool value, string message)
        {
            if (!value) throw new Exception("Mulching film check failed: " + message);
        }
        try
        {
            var root = new GameObject("MulchingChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var grid = root.AddComponent<GroundGrid>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(GroundGrid).GetMethod("Awake", flags).Invoke(grid, null);
            var clock = root.AddComponent<WorldClock>();
            var system = root.AddComponent<PotatoFarmingSystem>();
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", flags).SetValue(system, 0f);
            var cell = new Vector2Int(1, 1);
            Check(!system.TryApplyMulchingFilm(grid, cell), "unplanted tile rejected");
            system.TryStartFarming(grid, cell, clock);
            system.TryWater(grid, cell);
            Check(system.TryApplyMulchingFilm(grid, cell) && !system.TryApplyMulchingFilm(grid, cell),
                "film applied once before exposure");
            Check(system.HasMulchingFilm(grid, cell), "film can be queried by ground and cell");
            clock.AdvanceBy(80);
            Check(grid.GetCellState(cell) == GroundCellState.SoilCoveredFirst && !grid.CanCoverPotato(cell),
                "two-leaf potato stays hidden without cover interaction");
            system.TryGetFarmingTile(grid, cell, out var tile);
            var saved = (FarmCropData)tile.GetType().GetMethod("CaptureSaveData", flags).Invoke(tile, null);
            var restored = (PotatoFarmingSystem.FarmingTile)Activator.CreateInstance(tile.GetType(), flags,
                null, new object[] { saved, grid, clock, null }, null);
            Check(restored.IsMulchingFilmApplied, "film flag saved and restored");
            clock.AdvanceBy(70);
            Check(grid.GetCellState(cell) == GroundCellState.SoilCoveredSecond, "four-leaf potato stays hidden");
            clock.AdvanceBy(70);
            Check(tile.CurrentHealth == 100 && !tile.HasAbnormalStatus &&
                grid.GetCellState(cell) == GroundCellState.SoilCoveredThird, "deadline has no green or damage");
            clock.AdvanceBy(80);
            Check(tile.CurrentHealth == 100 && grid.GetCellState(cell) == GroundCellState.SoilCoveredFifth,
                "film persists through final growth without coordinator");
            system.StopFarming(grid, cell);
            system.TryStartFarming(grid, cell, clock);
            system.TryGetFarmingTile(grid, cell, out var replanted);
            Check(!replanted.IsMulchingFilmApplied, "new planting does not inherit film");
            system.TryWater(grid, cell);
            clock.AdvanceBy(80);
            Check(replanted.Stage == PotatoGrowthStage.TwoLargeLeaves &&
                !system.TryApplyMulchingFilm(grid, cell) && !system.HasMulchingFilm(grid, cell),
                "film is rejected after the early growth stages");
            Debug.Log("Mulching film checks passed.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [MenuItem("Tools/Project Potato/Validate Bug Infestation")]
    public static void RunBugs()
    {
        var randomState = UnityEngine.Random.state;
        var scene = EditorSceneManager.NewPreviewScene();
        void Check(bool value, string message)
        {
            if (!value) throw new Exception("Bug check failed: " + message);
        }
        try
        {
            var root = new GameObject("BugChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var grid = root.AddComponent<GroundGrid>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(GroundGrid).GetMethod("Awake", flags).Invoke(grid, null);
            var clock = root.AddComponent<WorldClock>();
            var system = root.AddComponent<PotatoFarmingSystem>();
            var chance = typeof(PotatoFarmingSystem).GetField("bugSpawnChance", flags);
            Check((float)chance.GetValue(system) == 0.3f, "default probability is 30 percent");
            chance.SetValue(system, 1f);
            var a = new Vector2Int(1, 1);
            var b = new Vector2Int(2, 2);
            var c = new Vector2Int(3, 3);
            foreach (var cell in new[] { a, b, c })
            {
                system.TryStartFarming(grid, cell, clock);
                system.TryWater(grid, cell);
            }
            system.TryGetFarmingTile(grid, a, out var exposed);
            system.TryGetFarmingTile(grid, b, out var removed);
            clock.AdvanceBy(79);
            Check(!exposed.HasBugs, "no bugs before two leaves");
            clock.AdvanceBy(1);
            Check(exposed.HasBugs && exposed.BugSecondsRemaining == 150 && !exposed.HasAbnormalStatus,
                "spawn is not itself an abnormal status");
            var saved = (FarmCropData)exposed.GetType().GetMethod("CaptureSaveData", flags).Invoke(exposed, null);
            var restored = (PotatoFarmingSystem.FarmingTile)Activator.CreateInstance(exposed.GetType(), flags,
                null, new object[] { saved, grid, clock, null }, null);
            Check(restored.HasBugs && restored.BugSecondsRemaining == 150, "save preserves live bugs and deadline");
            clock.AdvanceBy(149);
            Check(exposed.HasBugs && exposed.BugSecondsRemaining == 1 && exposed.Status == PotatoStatus.Green,
                "no bug damage before 150 seconds");
            Check(system.TryRemoveBugs(grid, b) && !system.TryRemoveBugs(grid, b), "chase succeeds once");
            Check(system.TryPauseGrowth(grid, c), "pause growth before deadline");
            clock.AdvanceBy(1);
            Check(!exposed.HasBugs && exposed.CurrentHealth == 80 &&
                exposed.Status == (PotatoStatus.Green | PotatoStatus.BugEaten), "expiry stacks with green");
            Check(removed.CurrentHealth == 90 && removed.Status == PotatoStatus.Green, "chased crop protected");
            Check(!system.TryRemoveBugs(grid, a), "late chase fails and does not cure");
            system.TryGetFarmingTile(grid, c, out var paused);
            Check(!paused.HasBugs && (paused.Status & PotatoStatus.BugEaten) != 0, "growth pause does not stop world timer");
            clock.AdvanceBy(500);
            system.RefreshGrowth();
            Check(exposed.CurrentHealth == 80 && !removed.HasBugs, "no repeated damage or respawn");
            var d = new Vector2Int(4, 4);
            system.TryStartFarming(grid, d, clock);
            system.TryWater(grid, d);
            clock.AdvanceBy(300);
            system.TryGetFarmingTile(grid, d, out var skipped);
            Check(!skipped.HasBugs && skipped.CurrentHealth == 80 &&
                (skipped.Status & PotatoStatus.BugEaten) != 0, "long time skip expires bugs correctly");
            chance.SetValue(system, 0f);
            var e = new Vector2Int(5, 5);
            system.TryStartFarming(grid, e, clock);
            system.TryWater(grid, e);
            clock.AdvanceBy(300);
            system.TryGetFarmingTile(grid, e, out var noSpawn);
            Check(!noSpawn.HasBugs && noSpawn.Status == PotatoStatus.Green, "failed roll never respawns");
            Debug.Log("Bug infestation checks passed.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Random.state = randomState;
        }
    }

    [MenuItem("Tools/Project Potato/Validate Soil Cover Deadline")]
    public static void RunSoilCoverDeadline()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        void Check(bool value, string message)
        {
            if (!value) throw new Exception("Soil cover check failed: " + message);
        }
        try
        {
            var root = new GameObject("SoilCoverChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var grid = root.AddComponent<GroundGrid>();
            typeof(GroundGrid).GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)?.Invoke(grid, null);
            var clock = root.AddComponent<WorldClock>();
            var system = root.AddComponent<PotatoFarmingSystem>();
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).SetValue(system, 0f);
            var harvest = root.AddComponent<PotatoHarvestCoordinator>();
            harvest.Configure(system);
            var a = new Vector2Int(1, 1);
            var b = new Vector2Int(2, 2);
            system.TryStartFarming(grid, a, clock);
            system.TryStartFarming(grid, b, clock);
            system.TryWater(grid, a);
            system.TryWater(grid, b);
            system.TryGetFarmingTile(grid, a, out var exposed);
            system.TryGetFarmingTile(grid, b, out var covered);
            clock.AdvanceBy(80);
            Check(grid.TryCoverPotato(b), "cover at two leaves");
            harvest.MarkCovered(grid, b);
            clock.AdvanceBy(139);
            Check(exposed.CurrentHealth == 100 && !exposed.HasAbnormalStatus, "no penalty before deadline");
            clock.AdvanceBy(1);
            Check(exposed.CurrentHealth == 90 && exposed.Status == PotatoStatus.Green,
                "exactly ten damage and green at six leaves");
            Check(covered.CurrentHealth == 100 && !covered.HasAbnormalStatus, "covered crop protected");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var saved = (FarmCropData)exposed.GetType().GetMethod("CaptureSaveData", flags).Invoke(exposed, null);
            var restored = (PotatoFarmingSystem.FarmingTile)Activator.CreateInstance(exposed.GetType(), flags,
                null, new object[] { saved, grid, clock, null }, null);
            Check(restored.Status == PotatoStatus.Green && saved.soilCoverDeadlineChecked &&
                restored.CurrentHealth == 90, "save round trip preserves penalty and status");
            Check(grid.TryCoverPotato(a), "late covering allowed");
            harvest.MarkCovered(grid, a);
            system.RefreshGrowth();
            clock.AdvanceBy(40);
            Check(exposed.CurrentHealth == 90 && exposed.HasAbnormalStatus, "late cover does not cure green");
            Check(harvest.TryCutForHarvest(grid, a) && exposed.CurrentHealth == 90,
                "optimal cutting does not repeat soil damage");
            Check(system.TryAddStatus(grid, a, PotatoStatus.BugEaten) &&
                exposed.Status == (PotatoStatus.Green | PotatoStatus.BugEaten), "conditions coexist independently of HP");
            Debug.Log("Soil cover deadline checks passed.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

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
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).SetValue(system, 0f);
            var a = new Vector2Int(1, 2);
            var b = new Vector2Int(3, 4);
            var c = new Vector2Int(5, 6);
            var d = new Vector2Int(7, 8);
            int objectsBefore = scene.GetRootGameObjects().Length;
            int notifications = 0;
            system.FarmingStageChanged += (g, cell, stage) =>
            {
                Check(g == ground && (cell == a || cell == b || cell == c || cell == d),
                    "event preserves grid and coordinates");
                notifications++;
            };
            Check(!system.TryStartFarming(null, a, clock), "missing grid rejected");
            Check(!system.TryStartFarming(ground, a, null), "missing clock rejected");
            Check(!system.TryStartFarming(ground, new Vector2Int(-1, 0), clock), "invalid cell rejected");
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
            Check(system.TryDamage(ground, b, 30f), "harvest test crop takes damage");
            Check(system.TryCompleteHarvest(ground, b, out float harvestedHp) && harvestedHp == 60f &&
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

            ground.TrySetCellState(d, GroundCellState.PlantingHole);
            Check(system.TryStartFarming(ground, d, clock), "pause test crop registered");
            Check(!system.TryPauseGrowth(ground, d), "unwatered crop cannot pause growth");
            Check(system.TryWater(ground, d), "pause test crop starts growing");
            system.TryGetFarmingTile(ground, d, out var pausedCrop);
            clock.AdvanceBy(10);
            Check(system.TryPauseGrowth(ground, d) && pausedCrop.IsGrowthPaused,
                "coordinate pauses one growing crop");
            Check(!system.TryPauseGrowth(ground, d), "already paused crop cannot pause twice");
            double ageBeforePauseWait = pausedCrop.AgeSeconds;
            PotatoGrowthStage stageBeforePauseWait = pausedCrop.Stage;
            clock.AdvanceBy(300);
            Check(pausedCrop.AgeSeconds == ageBeforePauseWait && pausedCrop.Stage == stageBeforePauseWait,
                "paused crop excludes elapsed world time and does not change stage");
            Check(system.TryResumeGrowth(ground, d) && !pausedCrop.IsGrowthPaused,
                "coordinate resumes paused crop");
            Check(!system.TryResumeGrowth(ground, d), "running crop cannot resume twice");
            clock.AdvanceBy(30);
            Check(pausedCrop.AgeSeconds >= 40d && pausedCrop.Stage == PotatoGrowthStage.Sprout,
                "resumed crop continues from its frozen growth age");
            Debug.Log($"[PotatoFarmingChecks] PASS: {checks} checks.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}

