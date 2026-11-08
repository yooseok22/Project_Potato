using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class WarehouseWithdrawalPlayChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task<string> Main()
    {
        if (!Application.isPlaying || !Application.isFocused) throw new Exception("Play mode and Editor focus required.");
        // Let scene Start callbacks and the initial saved-inventory binding finish first.
        await Task.Delay(200);
        int checks = 0;
        string stage = "prepare";
        void Check(bool value, string description)
        { if (!value) throw new Exception("Withdrawal play check: " + description); checks++; }
        void Update(object component) => component.GetType().GetMethod("Update", Flags).Invoke(component, null);
        var player = UnityEngine.Object.FindObjectsByType<FirstPersonPlayerController>(FindObjectsSortMode.None).First(p => p.IsLocallyControlled);
        var basket = player.GetComponent<PlayerBasketInventory>().Inventory;
        var interaction = player.GetComponent<PlayerInteractionController>();
        var withdrawal = player.GetComponent<WarehouseWithdrawalController>();
        var rig = player.ViewCamera;
        var camera = rig.GetComponent<Camera>();
        var blocker = player.GetComponent<PlayerActionBlocker>();
        var items = player.GetComponent<PlayerItemController>();
        var shelf = UnityEngine.Object.FindObjectsByType<WarehouseShelf>(FindObjectsSortMode.None)
            .OrderBy(s => s.GetComponent<Renderer>().bounds.center.y).ThenBy(s => s.ShelfId).First();
        var beforeShelf = shelf.Inventory.CaptureInventory();
        var beforeBasket = basket.CaptureInventory();
        var controller = SaveGameController.Instance;
        var slotsField = typeof(SaveGameController).GetField("<Slots>k__BackingField", Flags);
        var previousSlots = controller.Slots;
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WarehouseWithdrawalPlayChecks"));
        var testSlots = new SaveSlotService(directory);
        testSlots.Register(controller.Inventories);
        testSlots.Register(new FarmingSaveSection());
        slotsField.SetValue(controller, testSlots);
        float oldTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        var previousKeyboard = Keyboard.current;
        var previousMouse = Mouse.current;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        var mouse = InputSystem.AddDevice<Mouse>();
        void Keys(params Key[] keys)
        { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        void Clear(PotatoInventory inventory) => inventory.RestoreInventory(new InventoryData { ownerId = inventory.OwnerId });
        void Aim()
        {
            var bounds = shelf.GetComponent<Renderer>().bounds;
            Vector3 normal = new Vector3(0f, 0f, Mathf.Sign(shelf.Warehouse.transform.TransformPoint(shelf.Warehouse.Interior.center).z - bounds.center.z));
            var character = player.GetComponent<CharacterController>();
            character.enabled = false;
            player.transform.position = new Vector3(bounds.center.x, 1.73f, bounds.center.z) + normal * 2.25f;
            character.enabled = true;
            rig.RefreshPose(true);
            Vector3 target = bounds.center + normal * 1.06f + Vector3.up * bounds.size.y * 0.22f;
            var rotation = Quaternion.LookRotation(target - camera.transform.position).eulerAngles;
            typeof(FirstPersonCamera).GetField("yaw", Flags).SetValue(rig, rotation.y);
            typeof(FirstPersonCamera).GetField("pitch", Flags).SetValue(rig, rotation.x > 180f ? rotation.x - 360f : rotation.x);
            rig.RefreshPose(true);
            player.SetBasketSelectionOpen(false);
            Physics.SyncTransforms();
        }
        void PressE(bool shift = false)
        {
            Aim();
            Keys(shift ? new[] { Key.LeftShift, Key.E } : new[] { Key.E });
            Update(interaction);
            Keys();
        }
        try
        {
            Clear(basket);
            Clear(shelf.Inventory);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.OneStar, 0, 10);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.TwoStar, 0, 10);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.ThreeStar, 0, 10);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.None, HarvestedPotatoCondition.Green | HarvestedPotatoCondition.Sprouted, 10);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.None, HarvestedPotatoCondition.BugEaten | HarvestedPotatoCondition.Injured, 10);
            items.SelectSlot(PlayerItemSlot.Hoe);
            PressE();
            Check(ReferenceEquals(interaction.CurrentTarget, shelf) && withdrawal.IsOpen, "real E input opens the aimed stocked shelf");
            Check(blocker.GameplayBlocked && Cursor.lockState == CursorLockMode.None && !rig.enabled, "selection unlocks pointer and blocks movement and tools");
            Check(items.SelectedSlot == PlayerItemSlot.Hands, "selected tool hidden");
            var prop = player.transform.Find("PrototypePotatoBasket");
            Check(prop && prop.localPosition.z < 0f, "basket remains behind the player");
            await Task.Delay(150);
            var view = player.GetComponent<WarehouseWithdrawalView>();
            stage = "physical hover; open=" + withdrawal.IsOpen;
            if (!withdrawal.IsOpen) throw new Exception("Selection closed during fixture setup: focus=" + Application.isFocused + ", blocks=" + blocker.ActiveReasons + ", shelf=" + shelf.Inventory.Count + ", basket=" + basket.Count);
            foreach (var potato in shelf.Inventory.Potatoes)
            {
                var root = GameObject.Find("StoredPotato_" + potato.Id).transform;
                var point = camera.WorldToScreenPoint(root.position);
                Check(point.z > 0f && point.x > 0f && point.x < camera.pixelWidth && point.y > 0f && point.y < camera.pixelHeight,
                    "all 50 physical potatoes fit in the game view");
                Check(view.Hover(point) == potato.Id, "pointer picks the correct individual");
            }
            var selected = shelf.Inventory.Potatoes[30];
            stage = "click abnormal potato";
            var selectedRoot = GameObject.Find("StoredPotato_" + selected.Id).transform;
            Vector2 selectedPosition = camera.WorldToScreenPoint(selectedRoot.position);
            view.SetHover(selected.Id, selectedPosition);
            Check(player.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t => t && t.name == "Description" && t.text != null && t.text.Contains(selected.DisplayName)), "hover reveals combined abnormal conditions");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = selectedPosition, buttons = 1 });
            InputSystem.Update();
            Update(withdrawal);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = selectedPosition });
            InputSystem.Update();
            Check(withdrawal.IsOpen && basket.Count == 1 && shelf.Inventory.Count == 49 && ReferenceEquals(basket.FindPotato(selected.Id), selected), "real mouse click transfers one selected potato and saves");
            Check(!withdrawal.TrySelect(selected.Id) && basket.Count == 1, "repeated click cannot duplicate");
            Keys(Key.Escape);
            stage = "escape";
            Update(player);
            Update(withdrawal);
            Keys();
            Check(!withdrawal.IsOpen && !blocker.GameplayBlocked && rig.enabled && items.SelectedSlot == PlayerItemSlot.Hoe && basket.Count == 1,
                "real Esc restores view and tool while retaining transferred potatoes");
            PressE();
            stage = "fill basket";
            Check(withdrawal.IsOpen, "reopen with a partly filled basket");
            while (basket.Count < 9) Check(withdrawal.TrySelect(shelf.Inventory.Potatoes[0].Id), "click adds one potato");
            Check(withdrawal.IsOpen, "nine potatoes do not close selection");
            Check(withdrawal.TrySelect(shelf.Inventory.Potatoes[0].Id) && basket.Count == 10 && !withdrawal.IsOpen && !blocker.GameplayBlocked,
                "tenth potato automatically closes selection");
            Check(player.CursorCapturedThisFrame && items.SelectedSlot == PlayerItemSlot.Hoe, "closing click is consumed before restored tools update");
            Check(!withdrawal.TryOpen(shelf) && shelf.Inventory.Count == 40, "full basket cannot reopen or remove another potato");
            Clear(basket);
            stage = "last shelf potato";
            Clear(shelf.Inventory);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.ThreeStar, 0, 1);
            PressE();
            Check(withdrawal.TrySelect(shelf.Inventory.Potatoes[0].Id) && !withdrawal.IsOpen && basket.Count == 1 && shelf.Inventory.Count == 0,
                "last shelf potato closes safely");
            shelf.Inventory.TryAdd(PotatoHarvestGrade.TwoStar, 0, 3);
            stage = "disable and stale state";
            PressE();
            Check(withdrawal.IsOpen, "selection opens for disable check");
            withdrawal.enabled = false;
            Check(!withdrawal.IsOpen && !blocker.GameplayBlocked && rig.enabled, "disabling component restores input and camera");
            withdrawal.enabled = true;
            PressE();
            player.enabled = false;
            Update(withdrawal);
            Check(!withdrawal.IsOpen && !blocker.GameplayBlocked && Cursor.lockState == CursorLockMode.None,
                "disabling the player releases selection without recapturing the cursor");
            player.enabled = true;
            PressE();
            basket.TryAdd(PotatoHarvestGrade.OneStar, 0, 1);
            Update(withdrawal);
            Check(!withdrawal.IsOpen, "external inventory revision closes stale selection");
            PressE();
            Check(withdrawal.TrySelect(shelf.Inventory.Potatoes[0].Id), "persist selected potato before reload");
            stage = "disk reload";
            int savedBasket = basket.Count, savedShelf = shelf.Inventory.Count;
            Check(controller.Load(1), "reload the temporary save slot");
            Update(withdrawal);
            Check(!withdrawal.IsOpen && basket.Count == savedBasket && shelf.Inventory.Count == savedShelf, "reload preserves committed counts and exits stale selection");
            Check(controller.Load(1) && basket.Count == savedBasket, "repeated load never duplicates withdrawn potatoes");
            PressE(true);
            stage = "deposit";
            Check(!withdrawal.IsOpen && basket.Count == 0 && shelf.Inventory.Count == savedShelf + savedBasket, "Shift+E preserves stocked-shelf deposit");
            Clear(shelf.Inventory);
            basket.TryAdd(PotatoHarvestGrade.OneStar, 0, 3);
            PressE();
            Check(!withdrawal.IsOpen && shelf.Inventory.Count == 3 && basket.Count == 0, "empty shelf E still deposits");

            Clear(shelf.Inventory);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.OneStar, 0, 10);
            stage = "preview";
            shelf.Inventory.TryAdd(PotatoHarvestGrade.TwoStar, 0, 10);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.ThreeStar, 0, 10);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.None, HarvestedPotatoCondition.Green | HarvestedPotatoCondition.Sprouted, 10);
            shelf.Inventory.TryAdd(PotatoHarvestGrade.None, HarvestedPotatoCondition.BugEaten | HarvestedPotatoCondition.Injured, 10);
            basket.TryAdd(PotatoHarvestGrade.ThreeStar, 0, 2);
            PressE();
            await Task.Delay(150);
            var preview = shelf.Inventory.Potatoes[32];
            view.SetHover(preview.Id, camera.WorldToScreenPoint(GameObject.Find("StoredPotato_" + preview.Id).transform.position));
            string result = $"PASS: {checks} play checks. Temporary saves only: {directory}. Preview left open for visual inspection; stop Play mode to discard fixture.";
            Debug.Log("[WarehouseWithdrawalPlayChecks] " + result);
            return result;
        }
        catch (Exception error)
        {
            withdrawal.Close();
            shelf.Inventory.RestoreInventory(beforeShelf);
            basket.RestoreInventory(beforeBasket);
            slotsField.SetValue(controller, previousSlots);
            Time.timeScale = oldTimeScale;
            throw new Exception("Stage " + stage + "; checks=" + checks + ": " + error, error);
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
            previousKeyboard?.MakeCurrent();
            previousMouse?.MakeCurrent();
        }
    }
}
