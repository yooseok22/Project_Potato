using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Deterministic checks in an isolated preview scene, without waiting five minutes.</summary>
public static class WorldGrowthChecks
{
    [MenuItem("Tools/Project Potato/Validate World and Potato Growth")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("World/Growth check failed: " + message);
            checks++;
        }
        try
        {
            var root = new GameObject("WorldGrowthChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var clock = root.AddComponent<WorldClock>();
            var potato = root.AddComponent<PotatoGrowth>();
            var clockSettings = new SerializedObject(clock);
            clockSettings.FindProperty("logTransitions").boolValue = false;
            clockSettings.ApplyModifiedPropertiesWithoutUndo();
            var cropSettings = new SerializedObject(potato);
            cropSettings.FindProperty("logStageChanges").boolValue = false;
            cropSettings.ApplyModifiedPropertiesWithoutUndo();

            double[] boundaries = { 40, 80, 150, 220, 260, 300 };
            for (int i = 0; i < boundaries.Length; i++)
            {
                Check((int)PotatoGrowth.StageAt(boundaries[i] - 0.001) == i, "before boundary " + boundaries[i]);
                Check((int)PotatoGrowth.StageAt(boundaries[i]) == i + 1, "at boundary " + boundaries[i]);
            }
            Check(clock.IsDay && clock.HourOfDay == 6, "starts at 06:00 daytime");
            Check(potato.Plant(clock), "plant");
            Check(!potato.Plant(clock), "cannot reset a growing plant");
            Check(!potato.TryHarvest(), "cannot harvest early");
            int readyCount = 0;
            int stageChanges = 0;
            potato.HarvestReady += () => readyCount++;
            potato.StageChanged += _ => stageChanges++;
            clock.Tick(39);
            Check(potato.Stage == PotatoGrowthStage.SoilMound, "normal ticking");
            clock.IsPaused = true;
            clock.Tick(100);
            Check(clock.ElapsedSeconds == 39 && potato.AgeSeconds == 39, "shared pause");
            clock.IsPaused = false;
            clock.Speed = 2;
            clock.Tick(0.5);
            Check(potato.Stage == PotatoGrowthStage.Sprout && potato.AgeSeconds == 40, "shared speed");
            clock.AdvanceBy(259.999);
            Check(!potato.IsReadyToHarvest, "not ready just before 300 seconds");
            clock.AdvanceBy(0.0011);
            Check(potato.IsReadyToHarvest && readyCount == 1 && stageChanges == 6, "all stages and ready event after skip");
            Check(!clock.IsDay, "sunset at 18:00");
            clock.AdvanceBy(600);
            Check(readyCount == 1 && potato.Progress == 1f, "mature crop stays ready");
            Check(potato.TryHarvest() && !potato.TryHarvest(), "single harvest");
            Check(potato.Plant(clock) && potato.AgeSeconds == 0, "replant at current time");
            potato.enabled = false;
            clock.AdvanceBy(80);
            potato.enabled = true;
            potato.RefreshGrowth();
            Check(potato.Stage == PotatoGrowthStage.TwoLargeLeaves, "disabled crop catches up");
            clockSettings.Update();
            clockSettings.FindProperty("dayLengthSeconds").floatValue = 1200;
            clockSettings.ApplyModifiedPropertiesWithoutUndo();
            Check(potato.AgeSeconds == 80, "calendar length does not change crop age");
            clock.AdvanceBy(220);
            Check(potato.IsReadyToHarvest, "300 seconds with a different day length");
            double before = clock.ElapsedSeconds;
            bool rejected = false;
            try { clock.AdvanceBy(-1); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && clock.ElapsedSeconds == before, "reject backward time");
            Debug.Log($"[WorldGrowthChecks] PASS: {checks} checks.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
