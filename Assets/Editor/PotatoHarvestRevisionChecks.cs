using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoHarvestRevisionChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Project Potato/Connect Potato Harvest Changes")]
    public static void ConnectScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        var scene = SceneManager.GetActiveScene();
        var systems = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PotatoFarmingSystem>(true)).ToArray();
        if (systems.Length == 0) throw new InvalidOperationException("The current scene needs PotatoFarmingSystem.");
        foreach (var system in systems)
        {
            var coordinator = system.GetComponent<PotatoHarvestCoordinator>();
            if (!coordinator) coordinator = Undo.AddComponent<PotatoHarvestCoordinator>(system.gameObject);
            coordinator.Configure(system);
            EditorUtility.SetDirty(coordinator);
        }
        foreach (var player in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FirstPersonPlayerController>(true)))
        {
            var trample = player.GetComponent<CropTrampleController>();
            if (!trample) trample = Undo.AddComponent<CropTrampleController>(player.gameObject);
            Undo.RecordObject(trample, "Connect crop trample damage");
            trample.Configure(systems[0]);
            EditorUtility.SetDirty(trample);
            if (PrefabUtility.IsPartOfPrefabInstance(trample)) PrefabUtility.RecordPrefabInstancePropertyModifications(trample);
        }
        var deadTile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fbx/Tile/v2_reallast/FBX_Farm Land GD F.prefab");
        if (!deadTile) throw new InvalidOperationException("Missing GD F tile prefab.");
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GroundTileRenderer>(true)))
        {
            var serialized = new SerializedObject(renderer);
            var prefabs = serialized.FindProperty("tilePrefabs");
            prefabs.arraySize = 45;
            prefabs.GetArrayElementAtIndex(44).objectReferenceValue = deadTile;
            serialized.ApplyModifiedProperties();
            if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Potato Harvest] 반복 밟힘, 수확 보상, GD F 연결 및 씬 저장 완료.");
    }

    [MenuItem("Tools/Project Potato/Validate Revised Potato Harvest")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var randomState = UnityEngine.Random.state;
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception("Revised harvest check failed: " + message);
            checks++;
        }
        try
        {
            var root = new GameObject("HarvestRevisionChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var grid = root.AddComponent<GroundGrid>();
            typeof(GroundGrid).GetMethod("Awake", PrivateInstance).Invoke(grid, null);
            var clock = root.AddComponent<WorldClock>();
            var farm = root.AddComponent<PotatoFarmingSystem>();
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", PrivateInstance).SetValue(farm, 0f);
            var harvest = root.AddComponent<PotatoHarvestCoordinator>();
            harvest.Configure(farm);
            var trample = root.AddComponent<CropTrampleController>();
            trample.Configure(farm);

            void Plant(Vector2Int cell, bool covered = true)
            {
                grid.TrySetCellState(cell, GroundCellState.PlantingHole);
                Check(farm.TryStartFarming(grid, cell, clock) && farm.TryWater(grid, cell), "plant and water " + cell);
                if (covered) harvest.MarkCovered(grid, cell);
            }
            void SetDepth(Vector2Int cell, int depth)
            {
                var saved = (FarmGridData)typeof(GroundGrid).GetMethod("CaptureSaveData", PrivateInstance).Invoke(grid, null);
                saved.cells.Single(c => c.x == cell.x && c.y == cell.y).exposureDepth = (byte)depth;
                typeof(GroundGrid).GetMethod("ValidateSaveData", PrivateInstance).Invoke(grid, new object[] { saved });
                typeof(GroundGrid).GetMethod("RestoreSaveData", PrivateInstance).Invoke(grid, new object[] { saved });
                Check(grid.GetExposureDepth(cell) == depth, "saved exposure depth restored");
            }

            // All six covered/uncovered harvest branches, with exact fixed 1/2/3-hit depths.
            for (int covered = 0; covered <= 1; covered++)
            for (int depth = 1; depth <= 3; depth++)
            {
                var cell = new Vector2Int(2 + depth, 2 + covered);
                Plant(cell, covered == 1);
                SetDepth(cell, depth);
                clock.AdvanceBy(220d);
                Check(harvest.TryCutForHarvest(grid, cell), "early sickle works");
                farm.TryGetFarmingTile(grid, cell, out var crop);
                double age = crop.AgeSeconds;
                clock.AdvanceBy(30d);
                Check(crop.IsGrowthPaused && crop.AgeSeconds == age, "cut freezes growth age");
                Check(crop.CurrentHealth == (covered == 1 ? 80f : 70f), "early and uncovered penalties apply once");
                for (int hit = 1; hit <= depth; hit++)
                {
                    Check(harvest.TryUsePotatoFork(grid, cell), "fork hit " + hit);
                    Check(harvest.CanHarvest(grid, cell) == (hit == depth), "visible exactly at predetermined depth");
                }
                Check(grid.GetExposureDepth(cell) == depth, "digging never rerolls depth");
                Check(harvest.TryUsePotatoFork(grid, cell) &&
                    grid.TryGetHarvestInfo(cell, out var quality, out _) && quality == PotatoHarvestQuality.Damaged &&
                    (crop.Status & PotatoStatus.Injured) != 0, "extra digging records injury immediately at every exposure depth");
                Check(harvest.TryUsePotatoFork(grid, cell) && grid.GetCellState(cell) == GroundCellState.HarvestClearedField,
                    "excessive digging discards crop into N_015");
                Check(TileVisualResolver.Resolve(grid, cell).Kind == 14 && !farm.TryGetFarmingTile(grid, cell, out _) &&
                    !harvest.TryHarvest(grid, cell, out _), "fixed N_015 visual and no ghost crop or reward");
            }

            foreach (var pair in new[] { (age: 260d, health: 100f), (age: 300d, health: 60f) })
            {
                var cell = new Vector2Int(8, pair.age == 260 ? 2 : 3);
                Plant(cell);
                clock.AdvanceBy(pair.age);
                Check(harvest.TryCutForHarvest(grid, cell), "optimal/late cut permitted");
                farm.TryGetFarmingTile(grid, cell, out var crop);
                Check(crop.CurrentHealth == pair.health, "optimal/late timing damage");
                SetDepth(cell, 1);
                Check(harvest.TryUsePotatoFork(grid, cell) && harvest.TryHarvest(grid, cell, out var result), "E harvest exposed potato");
                Check(grid.GetCellState(cell) == GroundCellState.HarvestClearedField && !harvest.TryHarvest(grid, cell, out _),
                    "E harvest returns N_015 and never duplicates");
                Check(TileVisualResolver.Resolve(grid, cell).Kind == 14 && !farm.TryGetFarmingTile(grid, cell, out _),
                    "harvest shows fixed N_015 and clears the crop");
                Check(grid.TryUseHoePrimary(cell) && grid.GetCellState(cell) == GroundCellState.PlantingHole,
                    "harvested N_015 can be tilled again");
            }

            var stepped = new Vector2Int(10, 2);
            Plant(stepped);
            farm.TryGetFarmingTile(grid, stepped, out var steppedCrop);
            Check(trample.SetFootContact(grid, stepped, true) && steppedCrop.CurrentHealth == 90f, "first entry deals ten damage");
            for (int frame = 0; frame < 5; frame++)
                Check(!trample.SetFootContact(grid, stepped, true) && steppedCrop.CurrentHealth == 90f, "standing never repeats per frame");
            for (int frame = 0; frame < 5; frame++)
            {
                trample.ObserveGroundContact(null, default, false, 0.016f);
                Check(!trample.ObserveGroundContact(grid, stepped, true, 0.016f) && steppedCrop.CurrentHealth == 90f,
                    "brief grounded flicker does not invent a new trample event");
            }
            for (int hit = 2; hit <= 10; hit++)
            {
                trample.SetFootContact(null, default, false);
                Check(trample.SetFootContact(grid, stepped, true) && steppedCrop.CurrentHealth == 100f - hit * 10f, "re-entry damage stacks");
            }
            Check(grid.GetCellState(stepped) == GroundCellState.BoarDugField && !farm.TryGetFarmingTile(grid, stepped, out _) &&
                !harvest.TryHarvest(grid, stepped, out _), "zero HP immediately becomes GD F and gives nothing");
            Check(grid.CanUseHoePrimary(stepped) && grid.TryUseHoePrimary(stepped) &&
                grid.GetCellState(stepped) == GroundCellState.PlantingHole, "hoe left click recovers GD F to G_001");
            Check(farm.TryStartFarming(grid, stepped, clock), "recovered cell can be replanted");
            farm.StopFarming(grid, stepped);

            var jumpCell = new Vector2Int(13, 2);
            Plant(jumpCell);
            farm.TryGetFarmingTile(grid, jumpCell, out var jumpCrop);
            Check(trample.ObserveGroundContact(grid, jumpCell, true, 0.016f) && jumpCrop.CurrentHealth == 90f, "jump test first entry");
            trample.ObserveGroundContact(null, default, false, 0.2f);
            Check(trample.ObserveGroundContact(grid, jumpCell, true, 0.016f) && jumpCrop.CurrentHealth == 80f,
                "real airborne landing on the same crop still damages again");
            farm.StopFarming(grid, jumpCell);

            // Death inside a growth callback must not be overwritten by the remainder of that callback.
            var deadline = new Vector2Int(11, 2);
            Plant(deadline, false);
            farm.TryDamage(grid, deadline, 90f);
            clock.AdvanceBy(220d);
            Check(grid.GetCellState(deadline) == GroundCellState.BoarDugField && !farm.TryGetFarmingTile(grid, deadline, out _),
                "soil exposure deadline death cannot revive its visuals");
            clock.AdvanceBy(500d);
            Check(grid.GetCellState(deadline) == GroundCellState.BoarDugField, "dead crop never resumes growth");

            // Reproduce the user's actual console case: optimal digging, but bugs + uncovered soil already cost 20 HP.
            var eightyCell = new Vector2Int(14, 2);
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", PrivateInstance).SetValue(farm, 1f);
            Plant(eightyCell, false);
            clock.AdvanceBy(260d);
            farm.TryGetFarmingTile(grid, eightyCell, out var eightyCrop);
            Check(eightyCrop.CurrentHealth == 80f && (eightyCrop.Status & (PotatoStatus.Green | PotatoStatus.BugEaten)) ==
                (PotatoStatus.Green | PotatoStatus.BugEaten), "uncovered soil and expired bugs each cause ten HP loss");
            Check(harvest.TryCutForHarvest(grid, eightyCell) && eightyCrop.CurrentHealth == 80f,
                "optimal sickle use itself does not turn full HP into eighty");
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", PrivateInstance).SetValue(farm, 0f);

            foreach (var hp in new[] { 90f, 99f, 100f })
            {
                Check(PotatoHarvestConditionRules.AbnormalCountAt(hp, 3, 0.3999f) == 3, "three-count first 40 percent");
                Check(PotatoHarvestConditionRules.AbnormalCountAt(hp, 3, 0.4f) == 2, "three-count second 40 percent");
                Check(PotatoHarvestConditionRules.AbnormalCountAt(hp, 3, 0.8f) == 1, "three-count last 20 percent");
                Check(PotatoHarvestConditionRules.AbnormalCountAt(hp, 2, 0.4999f) == 2 &&
                    PotatoHarvestConditionRules.AbnormalCountAt(hp, 2, 0.5f) == 1, "two-count 50/50 split");
                Check(PotatoHarvestConditionRules.AbnormalCountAt(hp, 1, 0.99f) == 1, "single-count always abnormal");
            }
            foreach (var hp in new[] { 80f, 89f })
                Check(PotatoHarvestConditionRules.AbnormalCountAt(hp, 2, 0.6999f) == 2 &&
                    PotatoHarvestConditionRules.AbnormalCountAt(hp, 2, 0.7f) == 1, "80-89 two-count 70/30 split");
            Check(PotatoHarvestConditionRules.AbnormalCountAt(79f, 1, 0.99f) == 1 &&
                PotatoHarvestConditionRules.AbnormalCountAt(1f, 1, 0f) == 1 &&
                PotatoHarvestConditionRules.AbnormalCountAt(0f, 0, 0f) == 0, "low HP and zero HP condition counts");
            Check(PotatoHarvestConditionRules.Roll(95f, 3, HarvestedPotatoCondition.None).All(s => s == HarvestedPotatoCondition.None),
                "normal crops never invent an abnormal condition");
            Check(PotatoHarvestConditionRules.Roll(95f, 3, HarvestedPotatoCondition.Rotten).All(s => s == HarvestedPotatoCondition.None),
                "rotten never appears as a harvest reward");

            UnityEngine.Random.InitState(20261025);
            foreach (var hp in new[] { 100f, 95f, 85f })
            {
                const int samples = 30000;
                PotatoHarvestRewardRules.GetCountRange(hp, out int minimum, out int maximum);
                var counts = new int[4];
                for (int i = 0; i < samples; i++) counts[PotatoHarvestRewardRules.RollCount(hp)]++;
                float expected = samples / (float)(maximum - minimum + 1);
                for (int count = minimum; count <= maximum; count++)
                    Check(Math.Abs(counts[count] - expected) < expected * 0.05f, "quantities are uniformly sampled at HP " + hp + ": " + count);
            }
            var starCounts = new int[4];
            bool mixedGrades = false;
            for (int i = 0; i < 30000; i++)
            {
                var grades = PotatoHarvestRewardRules.Roll(100f);
                mixedGrades |= grades.Distinct().Count() > 1;
                foreach (var grade in grades) starCounts[(int)grade]++;
            }
            int total = starCounts.Sum();
            Check(mixedGrades && Math.Abs(starCounts[1] / (float)total - 0.7f) < 0.015f &&
                Math.Abs(starCounts[2] / (float)total - 0.2f) < 0.015f &&
                Math.Abs(starCounts[3] / (float)total - 0.1f) < 0.015f,
                "each potato has an independent grade roll retaining 70/20/10 percent weights");
            var combined = HarvestedPotatoCondition.Green | HarvestedPotatoCondition.BugEaten | HarvestedPotatoCondition.Sprouted;
            for (int sample = 0; sample < 20; sample++)
            {
                var statuses = PotatoHarvestConditionRules.Roll(95f, 3, combined);
                Check(statuses.Count(s => s != HarvestedPotatoCondition.None) >= 1 &&
                    statuses.All(s => s == HarvestedPotatoCondition.None || s == combined), "per-potato conditions preserve crop flags on selected subset");
            }

            // Each condition overrides every star grade, and combined conditions still describe one potato.
            var rewardGrades = new[] { PotatoHarvestGrade.OneStar, PotatoHarvestGrade.TwoStar, PotatoHarvestGrade.ThreeStar };
            foreach (var condition in new[] { HarvestedPotatoCondition.BugEaten, HarvestedPotatoCondition.Green,
                HarvestedPotatoCondition.Injured, HarvestedPotatoCondition.Sprouted, PotatoHarvestConditionRules.Harvestable })
            foreach (var grade in rewardGrades)
            {
                var abnormal = new PotatoHarvestResult(grid, default, PotatoHarvestTiming.Optimal,
                    PotatoHarvestQuality.Normal, "test", 100f, 100f, new[] { grade },
                    itemConditions: new[] { condition });
                Check(abnormal.Count == 1 && abnormal.AbnormalCount == 1 && abnormal.HasAbnormalStatus &&
                    abnormal.Grades[0] == PotatoHarvestGrade.None && abnormal.ItemConditions[0] == condition,
                    condition + " overrides " + grade + " without losing condition flags or duplicating the potato");
            }
            var mixed = new PotatoHarvestResult(grid, default, PotatoHarvestTiming.Optimal,
                PotatoHarvestQuality.Normal, "test", 100f, 100f,
                new[] { PotatoHarvestGrade.OneStar, PotatoHarvestGrade.TwoStar, PotatoHarvestGrade.ThreeStar,
                    PotatoHarvestGrade.OneStar, PotatoHarvestGrade.TwoStar, PotatoHarvestGrade.ThreeStar,
                    PotatoHarvestGrade.ThreeStar },
                itemConditions: new[] { HarvestedPotatoCondition.None, HarvestedPotatoCondition.None,
                    HarvestedPotatoCondition.None, HarvestedPotatoCondition.BugEaten, HarvestedPotatoCondition.Green,
                    HarvestedPotatoCondition.Injured, HarvestedPotatoCondition.Sprouted | HarvestedPotatoCondition.Green });
            Check(mixed.Count == 7 && mixed.AbnormalCount == 4 &&
                rewardGrades.All(g => mixed.Grades.Count(actual => actual == g) == 1) &&
                mixed.Grades.Count(g => g != PotatoHarvestGrade.None) + mixed.AbnormalCount == mixed.Count,
                "mixed harvest has disjoint star categories and abnormal category with a conserved total");
            var normalOnly = new PotatoHarvestResult(grid, default, PotatoHarvestTiming.Optimal,
                PotatoHarvestQuality.Normal, "test", 100f, 100f, rewardGrades);
            Check(normalOnly.Grades.SequenceEqual(rewardGrades) && normalOnly.AbnormalCount == 0 && !normalOnly.HasAbnormalStatus,
                "normal rewards retain their original star grades");
            string classificationLog = null;
            void CaptureClassificationLog(string message, string stackTrace, LogType type)
            {
                if (message.StartsWith("[Potato Harvest]")) classificationLog = message;
            }
            Application.logMessageReceived += CaptureClassificationLog;
            try { typeof(PotatoHarvestCoordinator).GetMethod("LogResult", PrivateInstance).Invoke(harvest, new object[] { mixed }); }
            finally { Application.logMessageReceived -= CaptureClassificationLog; }
            Check(classificationLog != null && classificationLog.Contains("수확개수=7") &&
                classificationLog.Contains("1성 1개, 2성 1개, 3성 1개") && classificationLog.Contains("상태이상 4개") &&
                classificationLog.Contains("상태이상 감자 (녹색+싹난)") && !classificationLog.Contains("0성"),
                "console reports abnormal rewards without star labels or duplicate star counts");

            // Use the production save section to preserve cut age, injury, health and dig depth together.
            var savedCell = new Vector2Int(12, 2);
            Plant(savedCell);
            SetDepth(savedCell, 3);
            clock.AdvanceBy(260d);
            harvest.TryCutForHarvest(grid, savedCell);
            Check(harvest.TryAddHarvestCondition(grid, savedCell, HarvestedPotatoCondition.Sprouted) &&
                !harvest.TryAddHarvestCondition(grid, savedCell, HarvestedPotatoCondition.Rotten), "sprouting can connect through harvest API; rotten is rejected");
            harvest.TryUsePotatoFork(grid, savedCell);
            var section = new FarmingSaveSection(() => new[] { grid }, () => new[] { clock }, () => new[] { farm }, () => new[] { harvest });
            string saved = section.Capture();
            farm.TryDamage(grid, savedCell, 10f);
            harvest.TryUsePotatoFork(grid, savedCell);
            section.PrepareRestore(1, saved)();
            Check(grid.GetExposureDepth(savedCell) == 3 && grid.GetCellState(savedCell) == GroundCellState.CoveredHighHidden &&
                farm.TryGetFarmingTile(grid, savedCell, out var restored) && restored.IsGrowthPaused && restored.CurrentHealth == 100f,
                "save restores depth, first dig, paused growth and health");
            Check(harvest.TryUsePotatoFork(grid, savedCell) && !harvest.CanHarvest(grid, savedCell) &&
                harvest.TryUsePotatoFork(grid, savedCell) && harvest.CanHarvest(grid, savedCell), "saved depth still exposes at third dig");
            Check(harvest.TryHarvest(grid, savedCell, out var final) && final.Count >= 2 && final.Count <= 3 && final.AbnormalCount >= 1 &&
                final.HasAbnormalStatus && final.ItemConditions.Any(s => s == HarvestedPotatoCondition.Sprouted),
                "saved sprouted condition reaches individual harvest rewards without inventing a sprouting timer");
            Check(Enumerable.Range(0, final.Count).All(i =>
                (final.Grades[i] == PotatoHarvestGrade.None) == (final.ItemConditions[i] != HarvestedPotatoCondition.None)),
                "real harvest after save/load classifies every potato as either starred or abnormal");
            Debug.Log($"[PotatoHarvestRevisionChecks] PASS: {checks} checks. Fixed dig depths, cut timing, repeated trampling, zero-HP recovery, N_015 discard, per-item conditions and save/load.");
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
