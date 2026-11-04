using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BasketProcessingChecks
{
    [MenuItem("Tools/Project Potato/Validate Processing Basket")]
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string description)
        { if (!condition) throw new Exception("Processing basket: " + description); checks++; }
        var random = UnityEngine.Random.state;
        var scene = EditorSceneManager.NewPreviewScene();
        string tempRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp"));
        string directory = Path.GetFullPath(Path.Combine(tempRoot, "BasketProcessingChecks-" + Guid.NewGuid().ToString("N")));
        try
        {
            for (int count = 0; count <= 10; count++)
            {
                var inventory = new PotatoInventory("selection-test", 10);
                inventory.TryAdd(PotatoHarvestGrade.OneStar, 0, count);
                var selection = new BasketPotatoSelection();
                selection.Reroll(inventory);
                for (int roll = 0; roll < 20; roll++)
                {
                    var previous = selection.Visible.Select(p => p.Id).ToHashSet();
                    selection.Reroll(inventory);
                    var now = selection.Visible.Select(p => p.Id).ToHashSet();
                    Check(selection.Visible.Count == Math.Min(3, count) && now.Count == selection.Visible.Count, "unique, physical potato count " + count);
                    Check(now.Intersect(previous).Count() == Math.Max(0, Math.Min(3, count) - (count - previous.Count)), "reuse only the unavoidable deficit " + count);
                    Check(selection.Visible.All(p => inventory.FindPotato(p.Id) == p), "selection references real inventory objects");
                }
            }
            var six = new PotatoInventory("six", 10);
            six.TryAdd(PotatoHarvestGrade.OneStar, 0, 6);
            var alternation = new BasketPotatoSelection();
            alternation.Reroll(six);
            var first = alternation.Visible.Select(p => p.Id).ToHashSet();
            alternation.Reroll(six);
            alternation.Reroll(six);
            Check(alternation.Visible.All(p => first.Contains(p.Id)), "round-one potatoes become eligible on round three");
            long consumed = alternation.Visible[0].Id;
            Check(six.TryConsume(consumed, () => true), "consume one selected individual");
            alternation.Remove(consumed);
            Check(alternation.Visible.Count == 2 && six.Count == 5, "no automatic replacement after clicking");
            alternation.Reroll(six);
            Check(alternation.Visible.All(p => p.Id != consumed), "consumed potato never reappears");

            // Build real animation drivers and carry rigs in an isolated preview scene.
            var root = new GameObject("BasketProcessingValidation");
            SceneManager.MoveGameObjectToScene(root, scene);
            var blender = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Fbx/Food Processing/Potato Millionaire Strach Blender 1.fbx"), root.transform);
            var maker = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Fbx/Food Processing/Potato Millionaire Strach Maker 1.fbx"), root.transform);
            var clock = root.AddComponent<WorldClock>();
            clock.Speed = 1f;
            clock.IsPaused = false;
            var configure = typeof(PotatoStarchProcessingChecks).GetMethod("ConfigureStation", BindingFlags.NonPublic | BindingFlags.Static);
            var processor = (PotatoStarchProcessor)configure.Invoke(null, new object[] { root, blender, maker, clock, false });
            var basket = new PotatoInventory("transaction-basket", 10);
            basket.TryAdd(PotatoHarvestGrade.OneStar, 0, 1);
            basket.TryAdd(PotatoHarvestGrade.TwoStar, 0, 1);
            basket.TryAdd(PotatoHarvestGrade.ThreeStar, 0, 2);
            basket.TryAdd(PotatoHarvestGrade.None, 0, 1);
            basket.TryAdd(PotatoHarvestGrade.ThreeStar, HarvestedPotatoCondition.Sprouted | HarvestedPotatoCondition.Green, 1);
            var section = new InventorySaveSection();
            section.Bind(basket);
            section.Bind(processor);
            var slots = new SaveSlotService(directory);
            slots.Register(section);
            bool Save() { slots.Save(1); return true; }
            Check(processor.TryLoadBlender(), "E opens lid without consuming potatoes");
            clock.AdvanceBy(processor.BlenderAnimation.Length(StarchAnimationId.BlenderOpen));
            Check(processor.CanReceivePotato && processor.LoadedPotatoCount == 0 && basket.Count == 6, "opening waits for selection");
            var abnormal = basket.Potatoes.Single(p => p.Conditions != 0);
            Check(!PotatoProcessingService.TryInsert(basket, abnormal.Id, processor, Save, out string error) && error == "상태이상 감자는 넣을 수 없습니다." && basket.Count == 6, "abnormal click rejected without consumption");
            var plain = basket.Potatoes.Single(p => p.Grade == PotatoHarvestGrade.None && p.Conditions == 0);
            Check(!PotatoProcessingService.TryInsert(basket, plain.Id, processor, Save, out _) && processor.LoadedPotatoCount == 0, "ungraded plain potato rejected");
            long valid = basket.Potatoes.First(p => p.CanProcess).Id;
            var before = JsonUtility.ToJson(basket.CaptureInventory());
            Check(!PotatoProcessingService.TryInsert(basket, valid, processor, () => false, out _) &&
                basket.FindPotato(valid) != null && JsonUtility.ToJson(basket.CaptureInventory()) == before && processor.LoadedPotatoCount == 0, "save rejection preserves identity, quantity and processor");
            Check(!PotatoProcessingService.TryInsert(basket, valid, processor, () => throw new IOException("simulated failure"), out _) &&
                basket.FindPotato(valid) != null && processor.CanReceivePotato, "save exception rolls back both sides");
            for (int i = 1; i <= processor.PotatoesPerBatch; i++)
            {
                var potato = basket.Potatoes.First(p => p.CanProcess);
                Check(PotatoProcessingService.TryInsert(basket, potato.Id, processor, Save, out _) && basket.FindPotato(potato.Id) == null,
                    "accepted star potato removed exactly once");
                Check(!PotatoProcessingService.TryInsert(basket, potato.Id, processor, Save, out _), "double click cannot duplicate consumption");
                Check(processor.LoadedPotatoCount == i && basket.Count == 6 - i, "one click equals one inserted potato");
                var expected = JsonUtility.ToJson(basket.CaptureInventory());
                slots.Load(1);
                Check(processor.LoadedPotatoCount == i && JsonUtility.ToJson(basket.CaptureInventory()) == expected, "disk reload restores matching basket and partial/full processor");
                slots.Load(1);
                Check(basket.Count == 6 - i, "repeated load never resurrects consumed potatoes");
            }
            Check(basket.CaptureInventory().items.All(p => p.quantity > 0) && basket.Potatoes.All(p => !p.CanProcess), "zero-count stack entries removed from save data");
            Check(processor.BlenderState == StarchBlenderState.LidClosing, "fourth potato begins closing");
            clock.AdvanceBy(processor.BlenderAnimation.Length(StarchAnimationId.BlenderClose));
            Check(processor.TryStartGrinding(), "full real batch can grind");
            clock.AdvanceBy(processor.GrindingDuration);
            Save();
            slots.Load(1);
            Check(processor.BlenderState == StarchBlenderState.GroundReady && basket.Count == 2, "ground batch survives reload without returning source potatoes");
            Check(processor.TryTakeBlender(null) && processor.TryPourBatch(null), "existing carry and pour work after selection");
            Save();
            slots.Load(1);
            Check(processor.SieveFillCount == 1 && processor.BlenderState == StarchBlenderState.DockedEmpty && processor.LoadedPotatoCount == 0,
                "reload returns empty held blender home and keeps poured batch");
            // Original inventory-only files have no processor owner; they restore an empty station.
            var oldSection = new InventorySaveSection();
            oldSection.Bind(basket);
            var oldSlots = new SaveSlotService(directory);
            oldSlots.Register(oldSection);
            oldSlots.Save(2);
            slots.Load(2);
            Check(processor.BlenderState == StarchBlenderState.DockedEmpty && processor.SieveFillCount == 0, "original save format migrates without a new required section");
            Debug.Log($"[BasketProcessingChecks] PASS: {checks} checks. Physical rerolls, deficit fallback, eligibility, consumption, rollback, disk reload and original saves.");
        }
        finally
        {
            UnityEngine.Random.state = random;
            EditorSceneManager.ClosePreviewScene(scene);
            if (directory.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
