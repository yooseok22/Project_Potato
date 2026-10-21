using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class YoungSeokPotatoHarvestChecks
{
    [MenuItem("Tools/Project Potato/Validate YoungSeok Potato Harvest")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("YoungSeok harvest check failed: " + message);
            checks++;
        }

        try
        {
            var root = new GameObject("YoungSeokPotatoHarvestChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var ground = root.AddComponent<GroundGrid>();
            typeof(GroundGrid).GetMethod("Awake", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)?.Invoke(ground, null);
            var cell = new Vector2Int(2, 2);

            CheckTiming(GroundCellState.EarlyHarvest, PotatoHarvestTiming.Early, false);
            CheckTiming(GroundCellState.ReadyToHarvest, PotatoHarvestTiming.Optimal, false);
            CheckTiming(GroundCellState.LateHarvest, PotatoHarvestTiming.Late, false);
            CheckTiming(GroundCellState.SoilCoveredThird, PotatoHarvestTiming.Early, true);
            CheckTiming(GroundCellState.SoilCoveredFourth, PotatoHarvestTiming.Optimal, true);
            CheckTiming(GroundCellState.SoilCoveredFifth, PotatoHarvestTiming.Late, true);

            GroundCellState[] normalStates =
            {
                GroundCellState.UncoveredHighYield, GroundCellState.UncoveredMidYield,
                GroundCellState.UncoveredLowYield, GroundCellState.CoveredHighYield,
                GroundCellState.CoveredMidYield, GroundCellState.CoveredLowYield
            };
            foreach (GroundCellState state in normalStates)
            {
                ground.TrySetCellState(cell, state);
                Check(ground.TryGetHarvestInfo(cell, out var quality, out _)
                    && quality == PotatoHarvestQuality.Normal, state + " is a normal potato");
            }

            GroundCellState[] injuredStates =
            {
                GroundCellState.UncoveredMidEarly, GroundCellState.UncoveredLowEarly,
                GroundCellState.CoveredMidEarly, GroundCellState.CoveredLowEarly
            };
            foreach (GroundCellState state in injuredStates)
            {
                ground.TrySetCellState(cell, state);
                Check(ground.TryGetHarvestInfo(cell, out var quality, out _)
                    && quality == PotatoHarvestQuality.Damaged, state + " is an injured potato");
            }

            CheckCountRange(100f, 2, 3);
            CheckCountRange(99f, 1, 3);
            CheckCountRange(90f, 1, 3);
            CheckCountRange(89f, 1, 2);
            CheckCountRange(80f, 1, 2);
            CheckCountRange(79f, 1, 1);
            CheckCountRange(1f, 1, 1);
            CheckCountRange(0f, 0, 0);

            Check(PotatoHarvestRewardRules.GradeAt(100f, 0.6999f) == PotatoHarvestGrade.OneStar,
                "100 HP below 70% is one star");
            Check(PotatoHarvestRewardRules.GradeAt(100f, 0.70f) == PotatoHarvestGrade.TwoStar,
                "100 HP from 70% is two star");
            Check(PotatoHarvestRewardRules.GradeAt(100f, 0.90f) == PotatoHarvestGrade.ThreeStar,
                "100 HP from 90% is three star");
            Check(PotatoHarvestRewardRules.GradeAt(90f, 0.8499f) == PotatoHarvestGrade.OneStar,
                "90-99 HP below 85% is one star");
            Check(PotatoHarvestRewardRules.GradeAt(90f, 0.85f) == PotatoHarvestGrade.TwoStar,
                "90-99 HP from 85% is two star");
            Check(PotatoHarvestRewardRules.GradeAt(89f, 0.99f) == PotatoHarvestGrade.OneStar,
                "below 90 HP is always one star");

            var clock = root.AddComponent<WorldClock>();
            var farmingSystem = root.AddComponent<PotatoFarmingSystem>();
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).SetValue(farmingSystem, 0f);
            var coordinator = root.AddComponent<PotatoHarvestCoordinator>();
            coordinator.Configure(farmingSystem);
            var harvestCell = new Vector2Int(3, 3);
            ground.TrySetCellState(harvestCell, GroundCellState.PlantingHole);
            Check(farmingSystem.TryStartFarming(ground, harvestCell, clock),
                "harvest integration crop registers");
            Check(farmingSystem.TryWater(ground, harvestCell),
                "harvest integration crop starts growing");
            clock.AdvanceBy(220d);
            Check(coordinator.TryCutForHarvest(ground, harvestCell)
                && ground.GetCellState(harvestCell) == GroundCellState.UncoveredCut,
                "early GB harvest freezes growth and changes to GB F");
            Check(farmingSystem.TryGetFarmingTile(ground, harvestCell, out var cutCrop)
                && cutCrop.IsGrowthPaused
                && cutCrop.CurrentHealth == 70f,
                "early GB harvest applies 20 timing damage and 10 uncovered damage");
            ground.TrySetCellState(harvestCell, GroundCellState.UncoveredHighYield);
            Check(coordinator.TryHarvest(ground, harvestCell, out var harvestResult)
                && harvestResult.Timing == PotatoHarvestTiming.Early
                && harvestResult.Quality == PotatoHarvestQuality.Normal
                && harvestResult.Health == 70f
                && harvestResult.Count == 1
                && harvestResult.Grades[0] == PotatoHarvestGrade.OneStar
                && ground.GetCellState(harvestCell) == GroundCellState.PlantingHole,
                "E harvest returns HP, reward and G_001");
            Check(!coordinator.TryHarvest(ground, harvestCell, out _),
                "completed crop cannot pay a second reward");

            var coveredHarvestCell = new Vector2Int(4, 4);
            ground.TrySetCellState(coveredHarvestCell, GroundCellState.PlantingHole);
            Check(farmingSystem.TryStartFarming(ground, coveredHarvestCell, clock)
                && farmingSystem.TryWater(ground, coveredHarvestCell),
                "covered harvest crop registers and starts growing");
            coordinator.MarkCovered(ground, coveredHarvestCell);
            clock.AdvanceBy(220d);
            Check(ground.GetCellState(coveredHarvestCell) == GroundCellState.SoilCoveredThird,
                "covered crop reaches GN_003");
            Check(coordinator.TryCutForHarvest(ground, coveredHarvestCell)
                && ground.GetCellState(coveredHarvestCell) == GroundCellState.CoveredCut,
                "sickle changes GN_003 to GN F");
            Check(farmingSystem.TryGetFarmingTile(ground, coveredHarvestCell, out var coveredCutCrop)
                && coveredCutCrop.IsGrowthPaused
                && coveredCutCrop.CurrentHealth == 80f,
                "GN_003 pauses growth and applies only early harvest damage");

            Debug.Log($"[YoungSeokPotatoHarvestChecks] PASS: {checks} checks.");

            void CheckTiming(GroundCellState state, PotatoHarvestTiming expected, bool covered)
            {
                Check(PotatoHarvestCoordinator.TryGetHarvestTiming(state, out var actual, out bool actualCovered)
                    && actual == expected && actualCovered == covered,
                    state + " harvest timing");
            }

            void CheckCountRange(float health, int minimum, int maximum)
            {
                PotatoHarvestRewardRules.GetCountRange(health, out int actualMinimum, out int actualMaximum);
                Check(actualMinimum == minimum && actualMaximum == maximum,
                    health + " HP count range");
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
