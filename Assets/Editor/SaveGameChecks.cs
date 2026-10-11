using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Persistence round trips in an isolated scene and temporary directory; never touches player slots.</summary>
public static class SaveGameChecks
{
    private sealed class InventoryTarget : IInventorySaveTarget
    {
        public string OwnerId => "player-a";
        public InventoryData Data = new() { ownerId = "player-a" };
        public bool Reject;
        public bool FailOnce;
        public InventoryData CaptureInventory() => Data;
        public void ValidateInventory(InventoryData data) { if (Reject) throw new InvalidDataException("Test rejection."); }
        public void RestoreInventory(InventoryData data)
        {
            Data = data;
            if (FailOnce) { FailOnce = false; throw new Exception("Test apply failure."); }
        }
    }

    private sealed class ExtraSection : ISaveSection
    {
        public string Key => "future-feature";
        public int Version => 1;
        public string Capture() => "{\"value\":42}";
        public Action PrepareRestore(int version, string json) => () => { };
    }

    private static string Snapshot(Component component) => JsonUtility.ToJson(component.GetType().GetMethod("CaptureSaveData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null));

    [MenuItem("Tools/Project Potato/Validate Save and Load")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        string directory = Path.Combine(Path.GetTempPath(), "PotatoSaveChecks-" + Guid.NewGuid().ToString("N"));
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Save check failed: " + message);
            checks++;
        }
        void Rejected(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch { rejected = true; }
            Check(rejected, message);
        }
        try
        {
            var root = new GameObject("SaveChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var grid = root.AddComponent<GroundGrid>();
            typeof(GroundGrid).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(grid, null);
            var clock = root.AddComponent<WorldClock>();
            var system = root.AddComponent<PotatoFarmingSystem>();
            var harvest = root.AddComponent<PotatoHarvestCoordinator>();
            harvest.Configure(system);
            var farming = new FarmingSaveSection(() => new[] { grid }, () => new[] { clock }, () => new[] { system }, () => new[] { harvest });
            var inventories = new InventorySaveSection();
            SaveSlotService NewService(bool extra = false)
            {
                var service = new SaveSlotService(directory);
                service.Register(farming);
                service.Register(inventories);
                if (extra) service.Register(new ExtraSection());
                return service;
            }
            var slots = NewService(true);
            var target = new InventoryTarget();
            target.Data.items.Add(new InventoryItemData { itemId = "potato", quantity = 7, slot = 0, propertiesJson = "{\"grade\":2}" });
            target.Data.items.Add(new InventoryItemData { itemId = "hoe", quantity = 1, slot = 1, instanceId = "hoe-001", propertiesJson = "{\"durability\":83}" });
            inventories.Bind(target);
            var growing = new Vector2Int(1, 1);
            var cut = new Vector2Int(2, 2);
            var dry = new Vector2Int(3, 3);
            var dead = new Vector2Int(4, 4);
            var emptyField = new Vector2Int(5, 5);
            foreach (var cell in new[] { growing, cut, dry, dead })
            {
                grid.TrySetCellState(cell, GroundCellState.PlantingHole);
                grid.TryPlantPotato(cell);
                Check(system.TryStartFarming(grid, cell, clock), "register crop " + cell);
            }
            foreach (var cell in new[] { growing, cut, dead })
            {
                grid.TryWater(cell);
                system.TryWater(grid, cell);
            }
            system.TryDamage(grid, dead, 100);
            clock.AdvanceBy(260);
            system.RefreshGrowth();
            grid.TryUseHoePrimary(growing);
            harvest.MarkCovered(grid, growing);
            Check(harvest.TryCutForHarvest(grid, cut), "cut crop before save");
            system.TryDamage(grid, growing, 17);
            grid.TryDig(emptyField);
            clock.Speed = 2;
            clock.IsPaused = true;
            slots.Save(1);
            Check(slots.Exists(1) && !slots.Exists(2), "slot 1 only");
            var expectedGrid = Snapshot(grid);
            var expectedSystem = Snapshot(system);
            var expectedHarvest = Snapshot(harvest);
            var expectedClock = Snapshot(clock);
            clock.AdvanceBy(80);
            system.StopFarming(grid, dry);
            grid.TryFill(emptyField);
            target.Data.items[0].quantity = 99;
            slots.Save(2);
            slots.Load(1);
            Check(Snapshot(grid) == expectedGrid, "all grid states, cover counts, water and prepared soil restored");
            Check(Snapshot(system) == expectedSystem, "timestamps, pauses, stages and health restored");
            Check(Snapshot(harvest) == expectedHarvest, "harvest timing and covered bookkeeping restored");
            Check(Snapshot(clock) == expectedClock, "calendar, speed and pause restored");
            Check(target.Data.items.Count == 2 && target.Data.items[0].quantity == 7 && target.Data.items[1].instanceId == "hoe-001", "stacks and unique items restored");
            Check(target.Data.items[1].propertiesJson.Contains("83"), "opaque item metadata restored");
            slots.Load(1);
            Check(system.FarmingCount == 4 && target.Data.items.Count == 2, "repeated loads replace without duplication");
            slots.Load(2);
            Check(system.FarmingCount == 3 && target.Data.items[0].quantity == 99, "second slot independent");
            slots.Load(1);
            var bad = JsonUtility.FromJson<FarmingSaveData>(farming.Capture());
            bad.grids[0].width++;
            Rejected(() => farming.PrepareRestore(1, JsonUtility.ToJson(bad)), "grid mismatch rejected before mutation");
            Rejected(() => farming.PrepareRestore(2, farming.Capture()), "future farming schema rejected");
            Check(clock.ElapsedSeconds == 260 && system.FarmingCount == 4, "validation preserves current farm");
            target.Reject = true;
            Rejected(() => slots.Load(2), "inventory adapter validates before farm mutation");
            Check(clock.ElapsedSeconds == 260 && target.Data.items[0].quantity == 7, "no partial load after validation failure");
            target.Reject = false;
            target.FailOnce = true;
            Rejected(() => slots.Load(2), "adapter apply failure reported");
            Check(clock.ElapsedSeconds == 260 && target.Data.items[0].quantity == 7 && system.FarmingCount == 4, "apply failure rolls back farm and inventory");
            inventories.Unbind(target);
            slots.Load(2);
            var lateTarget = new InventoryTarget();
            inventories.Bind(lateTarget);
            Check(lateTarget.Data.items[0].quantity == 99, "late-bound player receives pending inventory");
            inventories.Unbind(lateTarget);
            inventories.SetUnboundInventory(new InventoryData { ownerId = "other-player" });
            slots.Save(2);
            slots.Load(1);
            Check(inventories.GetInventory("other-player") == null, "slot switch clears absent pending owners");
            inventories.Bind(lateTarget);
            slots.Load(1);
            Check(lateTarget.Data.items[0].quantity == 7, "rebound inventory uses selected slot");
            slots = NewService();
            slots.Load(1);
            slots.Save(2);
            Check(slots.Read(2).sections.Exists(entry => entry.key == "future-feature"), "unregistered section preserved after load/save");
            slots.Save(1);
            lateTarget.Data.items[0].quantity = 12;
            slots.Save(1);
            Check(File.Exists(slots.GetSlotPath(1) + ".bak"), "previous revision backed up");
            File.WriteAllText(slots.GetSlotPath(1), "{broken");
            Rejected(() => slots.Load(1), "corrupt save rejected");
            Check(lateTarget.Data.items[0].quantity == 12, "corrupt save does not alter inventory");
            slots.Load(1, true);
            Check(lateTarget.Data.items[0].quantity == 7, "explicit backup recovery");
            Rejected(() => slots.Save(0), "slot zero rejected");
            Rejected(() => slots.Load(3), "third slot rejected");
            Rejected(() => inventories.SetUnboundInventory(new InventoryData { ownerId = "invalid", items = new() { new InventoryItemData { itemId = "potato", quantity = -1 } } }), "negative item quantity rejected");
            Rejected(() => inventories.Bind(lateTarget), "duplicate inventory owner rejected");

            Check(system.TryGetFarmingTile(grid, cut, out var tile) && tile.IsGrowthPaused, "cut growth remains paused");
            double cutAge = tile.AgeSeconds;
            clock.AdvanceBy(100);
            system.RefreshGrowth();
            Check(tile.AgeSeconds == cutAge, "cut crop does not grow after load");
            Check(system.TryGetFarmingTile(grid, dry, out var dryTile) && !dryTile.IsWatered && dryTile.AgeSeconds == 0, "dry crop stays dry");
            Check(system.TryGetFarmingTile(grid, dead, out var deadTile) && deadTile.IsDead, "dead crop preserved");
            Check(grid.IsSoilCoveredPotato(growing), "covered crop continues with covered visual");
            Check(harvest.TryUsePotatoFork(grid, cut), "fork continues from saved cut state");
            if (!harvest.CanHarvest(grid, cut)) Check(harvest.TryUsePotatoFork(grid, cut), "fork continues through hidden branch");
            Check(harvest.CanHarvest(grid, cut) && harvest.TryHarvest(grid, cut, out _), "saved harvest can finish");
            UnityEngine.Object.DestroyImmediate(root);
            root = new GameObject("SaveChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            grid = root.AddComponent<GroundGrid>();
            typeof(GroundGrid).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(grid, null);
            clock = root.AddComponent<WorldClock>();
            system = root.AddComponent<PotatoFarmingSystem>();
            harvest = root.AddComponent<PotatoHarvestCoordinator>();
            harvest.Configure(system);
            inventories = new InventorySaveSection();
            slots = NewService();
            slots.Load(2);
            Check(system.FarmingCount == 4 && clock.ElapsedSeconds == 260 && Snapshot(grid) == expectedGrid, "fresh scene objects and service restore from disk");
            var freshTarget = new InventoryTarget();
            inventories.Bind(freshTarget);
            Check(freshTarget.Data.items[0].quantity == 7, "fresh player adapter receives saved contents");
            Debug.Log($"[SaveGameChecks] PASS: {checks} checks.");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}

