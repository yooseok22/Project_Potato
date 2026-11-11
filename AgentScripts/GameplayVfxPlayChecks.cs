using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

public static class GameplayVfxPlayChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags | BindingFlags.Public).Invoke(target, args);
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);

    // Run in a disposable Play session and exit Play afterwards. Save writes use Temp only.
    public static async Task<string> Main()
    {
        if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
        var controller = SaveGameController.Instance;
        var slots = new SaveSlotService(Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/GameplayVfxPlayChecks")));
        slots.Register(controller.Inventories);
        slots.Register(new FarmingSaveSection());
        Field(controller, "<Slots>k__BackingField", slots);
        foreach (var spawn in UnityEngine.Object.FindObjectsByType<BoarSpawnManager>()) spawn.enabled = false;
        foreach (var dog in UnityEngine.Object.FindObjectsByType<DogHelperController>()) dog.enabled = false;
        var farm = UnityEngine.Object.FindAnyObjectByType<PotatoFarmingSystem>();
        var ground = (GroundGrid)typeof(PotatoFarmingSystem).GetField("ground", Flags).GetValue(farm);
        var clock = (WorldClock)typeof(PotatoFarmingSystem).GetField("clock", Flags).GetValue(farm);
        var harvest = farm.GetComponent<PotatoHarvestCoordinator>();
        var vfx = farm.GetComponent<FarmingVfxController>();
        var player = UnityEngine.Object.FindObjectsByType<FirstPersonPlayerController>().First(p => p.IsLocallyControlled);
        player.enabled = false;
        player.ViewCamera.enabled = false;
        foreach (var renderer in player.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        var camera = player.ViewCamera.GetComponent<Camera>();
        clock.IsPaused = true;
        Time.timeScale = 0f;
        Field(farm, "bugSpawnChance", 0f);
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/VFX"));
        Directory.CreateDirectory(directory);
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("VFX play check: " + message); checks++; }
        int Count(string name) => farm.GetComponentsInChildren<ParticleVfxInstance>().Count(e => e.name.StartsWith(name));
        Vector2Int Free(int start)
        {
            for (int x = start; x < ground.Width; x++)
                for (int z = 24; z < ground.Depth; z++)
                    if (!farm.TryGetFarmingTile(ground, new Vector2Int(x, z), out _)) return new Vector2Int(x, z);
            throw new Exception("No free test cell");
        }
        void Plant(Vector2Int cell)
        {
            ground.TrySetCellState(cell, GroundCellState.PlantingHole);
            Check(farm.TryStartFarming(ground, cell, clock) && farm.TryWater(ground, cell), "plant real scene crop");
            harvest.MarkCovered(ground, cell);
        }
        void Capture(string name, Vector3 target, Vector3 offset)
        {
            camera.transform.SetPositionAndRotation(target + offset, Quaternion.LookRotation(-offset));
            camera.nearClipPlane = 0.05f;
            var texture = RenderTexture.GetTemporary(1280, 720, 24);
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                var png = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                png.Apply();
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), png.EncodeToPNG());
                UnityEngine.Object.Destroy(png);
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(texture); }
        }
        void Simulate(ParticleVfxInstance effect, float seconds)
        {
            foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(seconds, false, true);
        }
        var leafCell = Free(24);
        Plant(leafCell);
        clock.AdvanceBy(PotatoGrowthRules.ReadyToHarvestSeconds);
        Check(harvest.TryCutForHarvest(ground, leafCell) && Count("VFX_Leaf Crash") == 1, "live sickle emits once");
        var leaf = farm.GetComponentsInChildren<ParticleVfxInstance>().Single(e => e.name.StartsWith("VFX_Leaf Crash"));
        Simulate(leaf, 0.08f);
        Check(leaf.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount) >= 12, "visible leaf particles");
        Capture("LeafCrash", ground.CellCenter(leafCell) + Vector3.up * 0.8f, new Vector3(3f, 2f, -4f));
        while (!harvest.CanHarvest(ground, leafCell)) Check(harvest.TryUsePotatoFork(ground, leafCell), "dig");
        Check(harvest.TryUsePotatoFork(ground, leafCell) && Count("VFX_Potato Crash") == 0, "injury is not destruction");
        Check(harvest.TryUsePotatoFork(ground, leafCell) && Count("VFX_Potato Crash") == 1, "final discard emits");
        leaf.Release();
        var potato = farm.GetComponentsInChildren<ParticleVfxInstance>().Single(e => e.name.StartsWith("VFX_Potato Crash"));
        Simulate(potato, 0.1f);
        Capture("PotatoCrash", ground.CellCenter(leafCell) + Vector3.up * 0.4f, new Vector3(3f, 2f, -4f));
        potato.Release();

        var boarCell = Free(26);
        Plant(boarCell);
        var boar = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Animals/Boar.prefab"),
            ground.CellCenter(boarCell), Quaternion.identity).GetComponent<BoarController>();
        boar.enabled = false;
        boar.Initialize(farm, new[] { ground }, ground);
        farm.TryGetFarmingTile(ground, boarCell, out var crop);
        Field(boar, "cropTarget", crop);
        for (int hit = 0; hit < 3; hit++)
        {
            Field(boar, "nextCropAttackAt", 0f);
            Call(boar, "UpdateAttackingCrop");
            Check(Count("VFX_Boar Crash") == (hit == 2 ? 1 : 0), "real boar attack emits only on death");
        }
        boar.gameObject.SetActive(false);
        UnityEngine.Object.Destroy(boar.gameObject);
        var crash = farm.GetComponentsInChildren<ParticleVfxInstance>().Single(e => e.name.StartsWith("VFX_Boar Crash"));
        Simulate(crash, 0.1f);
        Capture("BoarCrash", ground.CellCenter(boarCell) + Vector3.up * 0.5f, new Vector3(3f, 2.5f, -4f));
        // Restart playback; natural lifetime below is verified without manual simulation.
        foreach (var system in crash.GetComponentsInChildren<ParticleSystem>()) { system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); system.Play(false); }

        Field(farm, "bugSpawnChance", 1f);
        var bugCell = Free(28);
        Plant(bugCell);
        clock.AdvanceBy(80);
        farm.TryGetFarmingTile(ground, bugCell, out var bugCrop);
        Check(bugCrop.HasBugs && vfx.ActiveBugCount >= 1, "actual scene bug state emits");
        var bug = farm.GetComponentsInChildren<ParticleVfxInstance>().First(e => e.name.StartsWith("VFX_Bug") &&
            Vector3.Distance(e.transform.position, ground.CellCenter(bugCell)) < 1f);
        Simulate(bug, 1.2f);
        Check(bug.GetComponentsInChildren<ParticleSystem>().Any(p => p.particleCount > 0), "bugs render particles");
        Capture("Bugs", ground.CellCenter(bugCell) + Vector3.up * 1f, new Vector3(2.5f, 1.5f, -3.5f));
        Check(farm.TryRemoveBugs(ground, bugCell) && bug.IsFading && bug.gameObject.activeSelf, "chasing fades runtime bug particles");

        var processor = UnityEngine.Object.FindAnyObjectByType<PotatoStarchProcessor>();
        Call(processor, "ResetCycle");
        var starch = processor.GetComponent<StarchWaterVfxController>();
        for (int batch = 1; batch <= 5; batch++)
        {
            Check(processor.TryLoadBlender(), "open real blender");
            clock.AdvanceBy(processor.BlenderAnimation.Length(StarchAnimationId.BlenderOpen));
            var inventory = new PotatoInventory("vfx-play", 10);
            inventory.TryAdd(PotatoHarvestGrade.OneStar, 0, processor.PotatoesPerBatch);
            for (int p = 0; p < processor.PotatoesPerBatch; p++)
                Check(PotatoProcessingService.TryInsert(inventory, inventory.Potatoes[0].Id, processor, () => true, out _), "insert potato");
            clock.AdvanceBy(processor.BlenderAnimation.Length(StarchAnimationId.BlenderClose));
            Check(processor.TryStartGrinding(), "grind");
            clock.AdvanceBy(processor.GrindingDuration);
            Check(processor.TryTakeBlender(null) && processor.TryPourBatch(null), "pour into real red basket");
            starch.RefreshVfx();
            Check(starch.IsEmitting, "red basket emits at batch " + batch);
            clock.AdvanceBy(processor.MakerAnimation.Length(StarchAnimationId.BasketFill) / processor.SieveBatchCapacity);
            Check(processor.TryReturnBlender(null), "return blender");
            if (batch == 1)
            {
                var saved = processor.CaptureInventory();
                Call(processor, "ResetCycle");
                starch.RefreshVfx();
                Check(!starch.IsEmitting, "reset stops drips");
                processor.RestoreInventory(saved);
                starch.RefreshVfx();
                Check(starch.IsEmitting, "restored partially filled basket resumes drips");
                var drip = processor.GetComponentInChildren<ParticleVfxInstance>();
                Simulate(drip, 1.2f);
                var particles = new ParticleSystem.Particle[100];
                var emitter = drip.GetComponentsInChildren<ParticleSystem>().Single(p => p.name == "VFX_Starch Water Drop 001");
                int emitted = emitter.GetParticles(particles);
                Vector3 velocity = emitted > 0 ? particles[0].velocity : Vector3.zero;
                if (emitter.main.simulationSpace == ParticleSystemSimulationSpace.Local)
                    velocity = emitter.transform.TransformDirection(velocity);
                Check(emitted > 0 && velocity.y < 0f, "starch falls downward");
                var anchor = (Transform)typeof(StarchWaterVfxController).GetField("emissionAnchor", Flags).GetValue(starch);
                Capture("StarchWaterDrop", anchor.position - Vector3.up * 0.3f, new Vector3(0f, 0.2f, -2.6f));
            }
        }
        clock.AdvanceBy(processor.DrainingDuration);
        starch.RefreshVfx();
        Check(!starch.IsEmitting, "wet drips stop after real drain");
        Time.timeScale = 1f;
        await Task.Delay(7000);
        Check(!crash, "one-shot naturally releases offscreen after children finish");
        Time.timeScale = 0f;
        return checks + " play checks passed. Screenshots: " + directory;
    }
}
