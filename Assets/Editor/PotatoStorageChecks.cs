using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoStorageChecks
{
    [MenuItem("Tools/Project Potato/Validate Basket and Warehouse")]
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Potato storage check failed: " + message);
            checks++;
        }
        var temporaryRoot = Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(temporaryRoot, "PotatoStorageChecks-" + Guid.NewGuid().ToString("N")));
        if (!path.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test directory must stay under the temporary cache.");
        var randomState = UnityEngine.Random.state;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var basket = new PotatoInventory("test-basket", 10);
            var shelf = new PotatoInventory("test-shelf-001", 50);
            Check(basket.TryAdd(PotatoHarvestGrade.None, HarvestedPotatoCondition.None, 2), "ordinary potatoes fit");
            foreach (var grade in new[] { PotatoHarvestGrade.OneStar, PotatoHarvestGrade.TwoStar, PotatoHarvestGrade.ThreeStar })
                Check(basket.TryAdd(grade, HarvestedPotatoCondition.None, 1), "all star grades fit");
            foreach (var condition in new[] { HarvestedPotatoCondition.BugEaten, HarvestedPotatoCondition.Green, HarvestedPotatoCondition.Injured,
                HarvestedPotatoCondition.Sprouted, HarvestedPotatoCondition.Green | HarvestedPotatoCondition.Sprouted })
                Check(basket.TryAdd(PotatoHarvestGrade.ThreeStar, condition, 1), "abnormal potatoes fit independently");
            Check(basket.Count == 10 && basket.Free == 0 && !basket.TryAdd(PotatoHarvestGrade.OneStar, 0, 1), "ten individual potatoes is a hard cap");
            Check(basket.Describe("바구니").Contains("상태이상 감자 (녹색+싹난): 1개") &&
                !basket.Describe("바구니").Contains("3성 감자 ("), "debug output preserves conditions and exclusive classification");
            var section = new InventorySaveSection();
            section.Bind(basket);
            section.Bind(shelf);
            var slots = new SaveSlotService(path);
            slots.Register(section);
            Check(shelf.TryAdd(PotatoHarvestGrade.TwoStar, 0, 48), "fill shelf to forty-eight");
            Check(PotatoStorageService.TryDeposit(basket, shelf, () => { slots.Save(1); return true; }, out int moved) &&
                moved == 2 && basket.Count == 8 && shelf.Count == 50, "partial transfer respects shelf capacity");
            Check(!PotatoStorageService.TryDeposit(basket, shelf, () => true, out moved) && moved == 0 && basket.Count == 8,
                "full shelf does not consume basket contents");
            var expectedBasket = JsonUtility.ToJson(basket.CaptureInventory());
            var expectedShelf = JsonUtility.ToJson(shelf.CaptureInventory());
            basket.RestoreInventory(new InventoryData { ownerId = basket.OwnerId });
            shelf.RestoreInventory(new InventoryData { ownerId = shelf.OwnerId });
            slots.Load(1);
            Check(JsonUtility.ToJson(basket.CaptureInventory()) == expectedBasket && JsonUtility.ToJson(shelf.CaptureInventory()) == expectedShelf,
                "one disk snapshot restores both inventories and every property");
            slots.Load(1);
            Check(basket.Count == 8 && shelf.Count == 50, "repeated load replaces instead of adding");
            var emptyShelf = new PotatoInventory("test-shelf-002", 50);
            Check(!PotatoStorageService.TryDeposit(basket, emptyShelf, () => false, out moved) && moved == 0 && emptyShelf.Count == 0 &&
                JsonUtility.ToJson(basket.CaptureInventory()) == expectedBasket, "save rejection rolls back both sides");
            Check(!PotatoStorageService.TryDeposit(basket, emptyShelf, () => throw new IOException("simulated write failure"), out moved) &&
                emptyShelf.Count == 0 && basket.Count == 8, "save exception also rolls back both sides");
            Check(PotatoStorageService.TryDeposit(basket, emptyShelf, () => true, out moved) && moved == 8 && basket.Count == 0 && emptyShelf.Count == 8,
                "mixed full-basket deposit preserves all remaining potatoes");
            var overflow = new InventoryData { ownerId = basket.OwnerId };
            overflow.items.Add(new InventoryItemData { itemId = "potato", quantity = 11 });
            bool rejected = false;
            try { basket.RestoreInventory(overflow); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && basket.Count == 0, "invalid saved capacity is rejected without mutation");
            var unknown = new InventoryData { ownerId = basket.OwnerId };
            unknown.items.Add(new InventoryItemData { itemId = "unknown-item", quantity = 1 });
            rejected = false;
            try { basket.RestoreInventory(unknown); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && basket.Count == 0, "unknown item is rejected without mutation");
            // Restore the exact save contract into newly-created adapters, including an initially unbound shelf.
            var restoredSection = new InventorySaveSection();
            var restoredSlots = new SaveSlotService(path);
            restoredSlots.Register(restoredSection);
            restoredSlots.Load(1);
            var newBasket = new PotatoInventory(basket.OwnerId, 10);
            var newShelf = new PotatoInventory(shelf.OwnerId, 50);
            restoredSection.Bind(newBasket);
            restoredSection.Bind(newShelf);
            Check(newBasket.Count == 8 && newShelf.Count == 50, "late binding after reload restores stable owners");
            slots.Save(2);
            slots.Load(1);
            Check(basket.Count == 8, "slot one survives saving different slot two contents");

            var root = new GameObject("PotatoStorageHarvestChecks");
            SceneManager.MoveGameObjectToScene(root, scene);
            var grid = root.AddComponent<GroundGrid>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(GroundGrid).GetMethod("Awake", flags).Invoke(grid, null);
            var clock = root.AddComponent<WorldClock>();
            var farm = root.AddComponent<PotatoFarmingSystem>();
            typeof(PotatoFarmingSystem).GetField("bugSpawnChance", flags).SetValue(farm, 0f);
            var harvest = root.AddComponent<PotatoHarvestCoordinator>();
            harvest.Configure(farm);
            var cell = new Vector2Int(3, 3);
            grid.TrySetCellState(cell, GroundCellState.PlantingHole);
            farm.TryStartFarming(grid, cell, clock);
            farm.TryWater(grid, cell);
            harvest.MarkCovered(grid, cell);
            clock.AdvanceBy(260d);
            harvest.TryCutForHarvest(grid, cell);
            harvest.TryAddHarvestCondition(grid, cell, HarvestedPotatoCondition.Sprouted);
            for (int hit = 0; hit < 3 && !harvest.CanHarvest(grid, cell); hit++) harvest.TryUsePotatoFork(grid, cell);
            Check(harvest.CanHarvest(grid, cell), "test crop is ready to harvest");
            var farmSection = new FarmingSaveSection(() => new[] { grid }, () => new[] { clock }, () => new[] { farm }, () => new[] { harvest });
            basket.RestoreInventory(new InventoryData { ownerId = basket.OwnerId });
            basket.TryAdd(PotatoHarvestGrade.OneStar, 0, 8);
            Check(!PotatoStorageService.TryHarvest(basket, harvest, grid, cell, () => true, out _, farmSection) && harvest.CanHarvest(grid, cell) && basket.Count == 8,
                "insufficient maximum-yield room never removes the crop");
            basket.RestoreInventory(new InventoryData { ownerId = basket.OwnerId });
            Check(!PotatoStorageService.TryHarvest(basket, harvest, grid, cell, () => false, out _, farmSection) && harvest.CanHarvest(grid, cell) && basket.Count == 0,
                "failed harvest save restores the crop and basket");
            Check(PotatoStorageService.TryHarvest(basket, harvest, grid, cell, () => true, out string harvestError, farmSection) && !harvest.CanHarvest(grid, cell) &&
                basket.Count >= 2 && basket.Count <= 3 && basket.Describe("바구니").Contains("상태이상 감자 (싹난)"),
                "successful harvest deposits individual classified potatoes: " + harvestError);
            int paid = basket.Count;
            Check(!PotatoStorageService.TryHarvest(basket, harvest, grid, cell, () => true, out _, farmSection) && basket.Count == paid,
                "completed harvest never pays twice");
            Debug.Log($"[PotatoStorageChecks] PASS: {checks} checks. Mixed potatoes, capacities, partial deposits, disk reload, rollback and harvest safety.");
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            EditorSceneManager.ClosePreviewScene(scene);
            // Only our unique test directory is ever removed.
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}
