using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class WarehouseWithdrawalChecks
{
    [MenuItem("Tools/Project Potato/Validate Warehouse Withdrawal")]
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string description)
        { if (!condition) throw new Exception("Warehouse withdrawal: " + description); checks++; }
        string temp = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp"));
        string directory = Path.Combine(temp, "WarehouseWithdrawalChecks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var shelf = new PotatoInventory("withdrawal-shelf", 50);
            var basket = new PotatoInventory("withdrawal-basket", 10);
            shelf.TryAdd(PotatoHarvestGrade.OneStar, 0, 1);
            shelf.TryAdd(PotatoHarvestGrade.TwoStar, 0, 1);
            shelf.TryAdd(PotatoHarvestGrade.ThreeStar, 0, 1);
            shelf.TryAdd(PotatoHarvestGrade.None, HarvestedPotatoCondition.Green | HarvestedPotatoCondition.Sprouted, 2);
            shelf.TryAdd(PotatoHarvestGrade.None, HarvestedPotatoCondition.BugEaten | HarvestedPotatoCondition.Injured, 2);
            shelf.TryAdd(PotatoHarvestGrade.None, HarvestedPotatoCondition.Rotten, 1);
            var originals = shelf.Potatoes.ToArray();
            string beforeShelf = JsonUtility.ToJson(shelf.CaptureInventory());
            string beforeBasket = JsonUtility.ToJson(basket.CaptureInventory());
            int saves = 0;
            bool Save() { saves++; return true; }
            Check(!PotatoStorageService.TryWithdraw(shelf, basket, -1, Save, out _) && saves == 0, "stale selection never saves");
            Check(!PotatoStorageService.TryWithdraw(shelf, shelf, originals[0].Id, Save, out _) && saves == 0, "self transfer rejected");
            Check(!PotatoStorageService.TryWithdraw(shelf, basket, originals[0].Id, null, out _), "missing persistence rejected");
            foreach (bool throws in new[] { false, true })
            {
                Check(!PotatoStorageService.TryWithdraw(shelf, basket, originals[1].Id,
                    () => throws ? throw new IOException("simulated withdrawal failure") : false, out string error) && !string.IsNullOrEmpty(error), "failed commit reported");
                Check(JsonUtility.ToJson(shelf.CaptureInventory()) == beforeShelf && JsonUtility.ToJson(basket.CaptureInventory()) == beforeBasket, "both inventories roll back");
                Check(shelf.Potatoes.SequenceEqual(originals), "failed save preserves the exact shelf identities and ordering");
            }
            foreach (var potato in originals.Reverse())
            {
                int count = basket.Count;
                Check(PotatoStorageService.TryWithdraw(shelf, basket, potato.Id, Save, out _) && basket.Count == count + 1 && shelf.Count == originals.Length - basket.Count, "one click moves one potato");
                Check(ReferenceEquals(basket.FindPotato(potato.Id), potato) && shelf.FindPotato(potato.Id) == null, "selected identity, grade and conditions preserved");
                Check(!PotatoStorageService.TryWithdraw(shelf, basket, potato.Id, Save, out _), "double click cannot duplicate a potato");
            }
            Check(saves == originals.Length && shelf.Count == 0, "each successful click commits once, including abnormal potatoes");
            shelf.TryAdd(PotatoHarvestGrade.ThreeStar, 0, 50);
            for (int i = 0; i < 2; i++)
                Check(PotatoStorageService.TryWithdraw(shelf, basket, shelf.Potatoes[0].Id, Save, out _), "partly filled basket uses remaining capacity");
            int beforeSaves = saves;
            Check(basket.Count == 10 && !PotatoStorageService.TryWithdraw(shelf, basket, shelf.Potatoes[0].Id, Save, out _) && shelf.Count == 48 && saves == beforeSaves, "full basket rejects without consuming or saving");

            var section = new InventorySaveSection();
            section.Bind(shelf);
            section.Bind(basket);
            var slots = new SaveSlotService(directory);
            slots.Register(section);
            slots.Save(1);
            var restoredShelf = new PotatoInventory(shelf.OwnerId, 50);
            var restoredBasket = new PotatoInventory(basket.OwnerId, 10);
            var restoredSection = new InventorySaveSection();
            restoredSection.Bind(restoredShelf);
            restoredSection.Bind(restoredBasket);
            var restoredSlots = new SaveSlotService(directory);
            restoredSlots.Register(restoredSection);
            restoredSlots.Load(1);
            Check(JsonUtility.ToJson(restoredShelf.CaptureInventory()) == JsonUtility.ToJson(shelf.CaptureInventory()) &&
                JsonUtility.ToJson(restoredBasket.CaptureInventory()) == JsonUtility.ToJson(basket.CaptureInventory()), "disk reload preserves both sides and all classifications");
            int moved;
            Check(PotatoStorageService.TryDeposit(restoredBasket, restoredShelf, () => true, out moved) && moved == 2 && restoredShelf.Count == 50 && restoredBasket.Count == 8, "existing partial deposit still works after withdrawal");
            Debug.Log($"[WarehouseWithdrawalChecks] PASS: {checks} checks. Individual choice, conditions, capacity, duplicate clicks, identity rollback and disk reload.");
        }
        finally
        {
            if (directory.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
