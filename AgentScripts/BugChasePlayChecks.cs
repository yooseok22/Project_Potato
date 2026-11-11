using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class BugChasePlayChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Call(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);
    private static void Field(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);

    // Uses a disposable Play session; exit Play afterwards. Saves are redirected to Temp.
    public static async Task<string> Main()
    {
        if (!Application.isPlaying || !Application.isFocused) throw new Exception("Play mode and Editor focus required.");
        // Allow scene Start callbacks, saved-state binding and tool visual setup to finish.
        await Task.Delay(300);
        var controller = SaveGameController.Instance;
        var slots = new SaveSlotService(Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/BugChasePlayChecks")));
        slots.Register(controller.Inventories);
        slots.Register(new FarmingSaveSection());
        Field(controller, "<Slots>k__BackingField", slots);
        Time.timeScale = 0f;
        var farm = UnityEngine.Object.FindAnyObjectByType<PotatoFarmingSystem>();
        var ground = (GroundGrid)typeof(PotatoFarmingSystem).GetField("ground", Flags).GetValue(farm);
        var clock = (WorldClock)typeof(PotatoFarmingSystem).GetField("clock", Flags).GetValue(farm);
        clock.IsPaused = true;
        Field(farm, "bugSpawnChance", 1f);
        var player = UnityEngine.Object.FindObjectsByType<FirstPersonPlayerController>().First(p => p.IsLocallyControlled);
        var items = player.GetComponent<PlayerItemController>();
        var tools = player.GetComponent<PlayerToolController>();
        var target = player.GetComponent<FarmingTargetSelector>();
        var interaction = player.GetComponent<PlayerInteractionController>();
        var blocker = player.GetComponent<PlayerActionBlocker>();
        var view = UnityEngine.Object.FindAnyObjectByType<WateringInteractionView>();
        var label = (TMPro.TMP_Text)typeof(WateringInteractionView).GetField("promptLabel", Flags).GetValue(view);
        var vfx = farm.GetComponent<FarmingVfxController>();
        player.enabled = false;
        player.ViewCamera.enabled = false;
        Field(player, "captured", true);
        Cursor.lockState = CursorLockMode.Locked;
        var camera = player.ViewCamera.GetComponent<Camera>();
        var previousKeyboard = Keyboard.current;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Bug chase play check: " + message); checks++; }
        void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        void PressE() { Keys(Key.E); Call(interaction, "Update"); Call(tools, "Update"); Keys(); }
        void Refresh() { Physics.SyncTransforms(); Call(interaction, "Update"); Call(tools, "Update"); }
        void Aim(Vector2Int cell, float distance = 2.8f)
        {
            var center = ground.CellCenter(cell);
            var character = player.GetComponent<CharacterController>();
            character.enabled = false;
            player.transform.position = center + Vector3.back * distance + Vector3.up * 1.05f;
            camera.transform.SetPositionAndRotation(center + Vector3.back * distance + Vector3.up * 1.8f,
                Quaternion.LookRotation(Vector3.forward * distance + Vector3.down * 1.8f));
            character.enabled = true;
            Refresh();
        }
        bool Bugs(Vector2Int cell) => farm.TryGetFarmingTile(ground, cell, out var crop) && crop.HasBugs;
        GameObject Block(string name, bool interactable)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = camera.transform.position + camera.transform.forward * 1.1f;
            go.transform.localScale = Vector3.one * 0.5f;
            if (interactable) go.AddComponent<InteractionTarget>();
            Refresh();
            return go;
        }
        try
        {
            var cells = Enumerable.Range(24, 7).Select(x => new Vector2Int(x, 26)).ToArray();
            foreach (var cell in cells)
            {
                Check(!farm.TryGetFarmingTile(ground, cell, out _), "fixture uses an unused cell");
                ground.TrySetCellState(cell, GroundCellState.PlantingHole);
                Check(farm.TryStartFarming(ground, cell, clock) && farm.TryWater(ground, cell), "plant and water");
            }
            clock.AdvanceBy(80);
            Keys();
            for (int slot = 0; slot < 5; slot++)
            {
                items.SelectSlot((PlayerItemSlot)slot);
                Aim(cells[slot]);
                Check(target.CanChaseBugs && view.IsVisible && label.text == "[E]  벌레 쫓기", "E UI shown for " + items.SelectedSlot);
                Check(!interaction.HasInteractionTarget, "crop is handled by farming input");
                Call(target, "LateUpdate");
                Call(player.GetComponent<SickleHarvestPreviewController>(), "LateUpdate");
                Check(target.CanChaseBugs, "selection survives tool preview update");
                if (slot == 0)
                {
                    await Task.Delay(150);
                    Check(view.IsVisible && label.text.Contains("벌레 쫓기"), "UI remains visible in normal frames; focus=" + player.HasGameplayFocus + ", target=" + target.CanChaseBugs + ", label=" + label.text);
                    string screenshot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/VFX/BugChasePrompt.png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(screenshot));
                    ScreenCapture.CaptureScreenshot(screenshot);
                    await Task.Delay(300);
                }
                int beforeBugs = vfx.ActiveBugCount;
                var state = ground.GetCellState(cells[slot]);
                PressE();
                Check(!Bugs(cells[slot]) && Bugs(cells[6]), "E removes only the aimed crop's bugs");
                Check(vfx.ActiveBugCount == beforeBugs - 1, "E immediately stops that crop's VFX");
                Check(ground.GetCellState(cells[slot]) == state, "same E cannot also cover or harvest");
                Check(!target.CanChaseBugs && (!view.IsVisible || !label.text.Contains("벌레 쫓기")), "chase prompt clears on success");
            }
            items.SelectSlot(PlayerItemSlot.Hands);
            Aim(cells[5], 8f);
            Check(!target.CanChaseBugs && !view.IsVisible, "out-of-range crop has no E prompt");
            PressE();
            Check(Bugs(cells[5]), "out-of-range E has no effect");
            Aim(cells[5]);
            var wall = Block("BugChaseWall", false);
            Check(!target.CanChaseBugs && !view.IsVisible, "wall hides E prompt");
            PressE();
            Check(Bugs(cells[5]), "E cannot chase through walls");
            wall.SetActive(false);
            UnityEngine.Object.Destroy(wall);
            Refresh();
            blocker.SetSettingsOpen(true);
            Refresh();
            Check(!view.IsVisible, "blocked gameplay hides prompt");
            PressE();
            Check(Bugs(cells[5]), "blocked gameplay cannot chase");
            blocker.SetSettingsOpen(false);
            Refresh();
            var interactable = Block("BugChaseOtherInteraction", true);
            Check(interaction.HasInteractionTarget && !view.IsVisible, "existing interactions retain E priority");
            PressE();
            Check(Bugs(cells[5]), "another interaction cannot also chase bugs");
            interactable.SetActive(false);
            UnityEngine.Object.Destroy(interactable);
            Aim(cells[5]);
            PressE();
            Check(!Bugs(cells[5]) && target.CanCover, "after chase, ordinary hands action is available");
            PressE();
            Check(ground.IsSoilCoveredPotato(cells[5]), "next E retains normal soil-cover action");
            Aim(cells[6]);
            farm.TryGetFarmingTile(ground, cells[6], out var expired);
            typeof(PotatoFarmingSystem.FarmingTile).GetProperty("BugExpiresAt", Flags).SetValue(expired, clock.ElapsedSeconds);
            Refresh();
            Check(!target.CanChaseBugs && !label.text.Contains("벌레 쫓기"), "expired bugs do not offer chasing");
            camera.transform.rotation = Quaternion.LookRotation(Vector3.up);
            Refresh();
            Check(!target.HasTarget && !target.CanChaseBugs && !view.IsVisible, "looking away clears target and UI");
            return checks + " E-key and UI checks passed; screenshot Screenshots/VFX/BugChasePrompt.png";
        }
        finally
        {
            Keys();
            InputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null) previousKeyboard.MakeCurrent();
            Time.timeScale = 1f;
        }
    }
}
