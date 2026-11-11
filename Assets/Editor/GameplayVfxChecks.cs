using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameplayVfxChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, Private).Invoke(target, args);
    private static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fbx/VFX/" + name + ".prefab");

    [MenuItem("Tools/Project Potato/Validate Gameplay VFX")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var random = UnityEngine.Random.state;
        FarmingVfxController vfx = null;
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception("Gameplay VFX check failed: " + message);
            checks++;
        }
        try
        {
            var root = new GameObject("GameplayVfxChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var ground = root.AddComponent<GroundGrid>();
            Call(ground, "Awake");
            var other = new GameObject("OtherGround");
            SceneManager.MoveGameObjectToScene(other, scene);
            var otherGround = other.AddComponent<GroundGrid>();
            Call(otherGround, "Awake");
            var clock = root.AddComponent<WorldClock>();
            var farm = root.AddComponent<PotatoFarmingSystem>();
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", Private).SetValue(farm, 1f);
            vfx = root.AddComponent<FarmingVfxController>();
            var harvest = root.GetComponent<PotatoHarvestCoordinator>();
            harvest.Configure(farm);
            vfx.Configure(Prefab("VFX_Boar Crash"), Prefab("VFX_Bug"), Prefab("VFX_Leaf Crash"), Prefab("VFX_Potato Crash"));
            Call(vfx, "OnEnable");
            int Count(string name) => root.GetComponentsInChildren<ParticleVfxInstance>().Count(e => e.name.StartsWith(name));
            void Plant(GroundGrid grid, Vector2Int cell)
            {
                grid.TrySetCellState(cell, GroundCellState.PlantingHole);
                Check(farm.TryStartFarming(grid, cell, clock) && farm.TryWater(grid, cell), "plant/water " + cell);
                harvest.MarkCovered(grid, cell);
            }
            var cell = new Vector2Int(2, 2);
            Plant(ground, cell);
            Plant(otherGround, cell);
            clock.AdvanceBy(80);
            Check(vfx.ActiveBugCount == 2, "same coordinates on different grounds own separate bug effects");
            farm.RefreshGrowth();
            Check(vfx.ActiveBugCount == 2, "refresh cannot duplicate bugs");
            Check(farm.TryRemoveBugs(ground, cell) && vfx.ActiveBugCount == 1, "chasing clears only that crop");
            Check(farm.TryDamage(otherGround, cell, 100f) && vfx.ActiveBugCount == 0, "crop death clears bugs");
            Check(Count("VFX_Boar Crash") == 0, "ordinary damage is not a boar destruction");

            var restoredCell = new Vector2Int(3, 2);
            Plant(ground, restoredCell);
            clock.AdvanceBy(80);
            Check(vfx.ActiveBugCount == 1, "new crop spawns bugs");
            var saved = (FarmSystemData)Call(farm, "CaptureSaveData");
            Check(farm.StopFarming(ground, restoredCell) && vfx.ActiveBugCount == 0, "discard/removal clears bugs immediately");
            Call(farm, "RestoreSaveData", saved,
                new Dictionary<string, GroundGrid> { [SaveIdentity.Key(ground)] = ground, [SaveIdentity.Key(otherGround)] = otherGround },
                new Dictionary<string, WorldClock> { [SaveIdentity.Key(clock)] = clock });
            Check(vfx.ActiveBugCount == 1 && Count("VFX_Leaf Crash") == 0 && Count("VFX_Potato Crash") == 0,
                "load restores live bugs without replaying one-shots");
            Check(farm.TryCompleteHarvest(ground, restoredCell, out _) && vfx.ActiveBugCount == 0, "harvest removes live bugs");

            var boarCell = new Vector2Int(4, 2);
            Plant(ground, boarCell);
            Check(farm.TryDamageFromBoar(ground, boarCell, 35f) && Count("VFX_Boar Crash") == 0, "nonfatal boar hit emits nothing");
            Check(farm.TryDamageFromBoar(ground, boarCell, 100f) && Count("VFX_Boar Crash") == 1, "fatal boar hit emits once despite removal callback");
            Check(!farm.TryDamageFromBoar(ground, boarCell, 100f) && Count("VFX_Boar Crash") == 1, "dead crop cannot emit another crash");

            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", Private).SetValue(farm, 0f);
            var cutCell = new Vector2Int(5, 2);
            Plant(ground, cutCell);
            clock.AdvanceBy(PotatoGrowthRules.ReadyToHarvestSeconds);
            Check(harvest.TryCutForHarvest(ground, cutCell) && Count("VFX_Leaf Crash") == 1, "successful sickle emits leaves");
            Check(!harvest.TryCutForHarvest(ground, cutCell) && Count("VFX_Leaf Crash") == 1, "failed/repeated sickle emits nothing");
            while (!harvest.CanHarvest(ground, cutCell)) Check(harvest.TryUsePotatoFork(ground, cutCell), "normal dig");
            Check(Count("VFX_Potato Crash") == 0, "normal digging emits no destruction");
            Check(harvest.TryUsePotatoFork(ground, cutCell) && Count("VFX_Potato Crash") == 0, "injury alone emits no destruction");
            Check(harvest.TryUsePotatoFork(ground, cutCell) && Count("VFX_Potato Crash") == 1, "final fork discard emits once");
            Check(!harvest.TryUsePotatoFork(ground, cutCell) && Count("VFX_Potato Crash") == 1, "empty cell emits nothing");
            foreach (var effect in root.GetComponentsInChildren<ParticleVfxInstance>())
            {
                var systems = effect.GetComponentsInChildren<ParticleSystem>(true);
                Check(systems.All(p => !p.main.loop && !p.main.playOnAwake), "all one-shot children are nonlooping and controlled");
                Check(effect.transform.position.x < 0f, "exported preview positions are not used");
                foreach (var system in systems) system.Simulate(0.2f, false, true);
                Check(systems.Any(p => p.particleCount > 0), "authored one-shot actually emits particles");
                Call(effect, "Update");
                Check(effect, "one-shot survives while child particles remain");
                foreach (var system in systems)
                {
                    system.Simulate(10f, false, true);
                    // Simulate leaves an editor-preview system paused, which still reports IsAlive.
                    system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
                Call(effect, "Update");
                Check(!effect, "finished one-shot releases all children");
            }

            // Use the real imported props and the real five-batch processing path.
            var station = new GameObject("StarchVfxChecks");
            SceneManager.MoveGameObjectToScene(station, scene);
            var blender = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Fbx/Food Processing/Potato Millionaire Strach Blender 1.fbx"), station.transform);
            var maker = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Fbx/Food Processing/Potato Millionaire Strach Maker 1.fbx"), station.transform);
            var setup = typeof(PotatoStarchProcessingChecks).GetMethod("ConfigureStation", BindingFlags.Static | BindingFlags.NonPublic);
            var processor = (PotatoStarchProcessor)setup.Invoke(null, new object[] { station, blender, maker, clock, false });
            Call(processor, "EnsureInitialized");
            var anchor = maker.GetComponentsInChildren<Transform>(true).Single(t => t.name == "B_Starch Water Basket" && t.parent.name == t.name);
            var starchVfx = station.AddComponent<StarchWaterVfxController>();
            starchVfx.Configure(processor, Prefab("VFX_Starch Water Drop"), anchor);
            starchVfx.RefreshVfx();
            Check(!starchVfx.IsEmitting, "empty red basket is off");
            for (int batch = 1; batch <= 5; batch++)
            {
                Check(processor.TryLoadBlender(), "open blender");
                clock.AdvanceBy(processor.BlenderAnimation.Length(StarchAnimationId.BlenderOpen));
                var inventory = new PotatoInventory("vfx-test", 10);
                inventory.TryAdd(PotatoHarvestGrade.OneStar, HarvestedPotatoCondition.None, processor.PotatoesPerBatch);
                for (int potato = 0; potato < processor.PotatoesPerBatch; potato++)
                    Check(PotatoProcessingService.TryInsert(inventory, inventory.Potatoes[0].Id, processor, () => true, out _), "load actual potato");
                clock.AdvanceBy(processor.BlenderAnimation.Length(StarchAnimationId.BlenderClose));
                Check(processor.TryStartGrinding(), "start blender");
                clock.AdvanceBy(processor.GrindingDuration);
                Check(processor.TryTakeBlender(null) && processor.TryPourBatch(null), "pour real batch");
                starchVfx.RefreshVfx();
                Check(starchVfx.IsEmitting, "partial fill drips, batch " + batch);
                clock.AdvanceBy(processor.MakerAnimation.Length(StarchAnimationId.BasketFill) / processor.SieveBatchCapacity);
                starchVfx.RefreshVfx();
                Check(starchVfx.IsEmitting && station.GetComponentsInChildren<ParticleVfxInstance>().Length == 1,
                    "waiting/full/draining keeps exactly one effect");
                Check(processor.TryReturnBlender(null), "return blender");
            }
            var drip = station.GetComponentInChildren<ParticleVfxInstance>();
            Check(drip.GetComponentsInChildren<ParticleSystem>().All(p => p.main.loop), "all drip emitters loop");
            anchor.position += Vector3.right;
            starchVfx.RefreshVfx();
            Check(Mathf.Abs(drip.transform.position.x - anchor.position.x) < 0.001f, "emitter follows the animated basket");
            clock.AdvanceBy(processor.DrainingDuration);
            starchVfx.RefreshVfx();
            Check(processor.SieveFillCount == 5 && !starchVfx.IsEmitting, "drain completion stops despite retained batch count");
            foreach (var state in new[] { StarchSieveState.WetStarchReady, StarchSieveState.ReadyToDry, StarchSieveState.Drying,
                StarchSieveState.ReadyToPack, StarchSieveState.DryHandlesMoving, StarchSieveState.DryDropping })
            {
                typeof(PotatoStarchProcessor).GetProperty("SieveState").SetValue(processor, state);
                starchVfx.RefreshVfx();
                Check(!starchVfx.IsEmitting, "no wet drips in " + state);
            }
            Debug.Log($"[GameplayVFX] {checks} checks passed.");
        }
        finally
        {
            if (vfx) Call(vfx, "OnDisable");
            UnityEngine.Random.state = random;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
