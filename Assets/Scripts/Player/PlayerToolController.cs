using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerItemController), typeof(FirstPersonPlayerController))]
[DefaultExecutionOrder(50)]
public sealed class PlayerToolController : MonoBehaviour
{
    [SerializeField] private FarmingTargetSelector farmingTarget;
    [SerializeField] private PotatoFarmingSystem farmingSystem;
    [SerializeField] private GameObject[] toolPrefabs = new GameObject[5];
    [SerializeField, Min(0.5f)] private float actionRange = 4f;
    [SerializeField, Min(1)] private int wateringCanCapacity = 8;
    [SerializeField] private bool infiniteWater = true;
    [SerializeField] private LayerMask actionMask = ~0;

    private FirstPersonPlayerController player;
    private PlayerItemController items;
    private PlayerVisualController visuals;
    private GameObject[] toolInstances;
    private int water;
    private WateringInteractionView wateringView;

    public int Water => water;
    public int WaterCapacity => wateringCanCapacity;
    public bool InfiniteWater => infiniteWater;

    private void Awake()
    {
        player = GetComponent<FirstPersonPlayerController>();
        items = GetComponent<PlayerItemController>();
        visuals = GetComponent<PlayerVisualController>();
        if (!farmingTarget) farmingTarget = GetComponent<FarmingTargetSelector>();
        if (!farmingSystem) farmingSystem = FindAnyObjectByType<PotatoFarmingSystem>();
        wateringView = FindAnyObjectByType<WateringInteractionView>();
        if (toolPrefabs == null || toolPrefabs.Length != 5) Array.Resize(ref toolPrefabs, 5);
    }

    private void Start()
    {
        CreateToolVisuals();
        items.SelectedSlotChanged += OnSlotChanged;
        OnSlotChanged(items.SelectedSlot);
    }

    private void Update()
    {
        UpdateFarmingPrompt();
        if (!player.CanUseGameplayInput || items.IsAimingThrow || player.CursorCapturedThisFrame) return;
        var keyboard = Keyboard.current;
        if (items.SelectedSlot == PlayerItemSlot.Hands && keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            farmingTarget?.TryUseHandsSelected(farmingSystem);
            UpdateFarmingPrompt();
        }
        var mouse = Mouse.current;
        if (mouse == null) return;
        bool primary = mouse.leftButton.wasPressedThisFrame;
        bool secondary = mouse.rightButton.wasPressedThisFrame;
        if (!primary && !secondary) return;

        switch (items.SelectedSlot)
        {
            case PlayerItemSlot.Hands:
                if (secondary && !items.HasItem(PlayerItemSlot.Hands)) TryPickUp();
                break;
            case PlayerItemSlot.Hoe:
                if (primary) farmingTarget?.TryUseHoePrimarySelected();
                else if (secondary) farmingTarget?.TryUseHoeSecondarySelected();
                break;
            case PlayerItemSlot.WateringCan:
                if (primary) TryWater();
                else if (secondary) TryRefill();
                break;
            case PlayerItemSlot.Sickle:
                if (primary && !(farmingTarget && farmingTarget.TryUseSickleSelected(farmingSystem)))
                    TryUseOnCrop(crop => crop.TryCutStem());
                break;
            case PlayerItemSlot.PotatoFork:
                if (primary && !(farmingTarget && farmingTarget.TryUsePotatoForkSelected()))
                    TryUseOnCrop(crop => crop.TryHarvestWithFork());
                break;
        }
    }

    private void TryPickUp()
    {
        if (!TryRaycast(out var hit)) return;
        var pickup = hit.collider.GetComponentInParent<PickupItem>();
        if (!pickup || !items.TryHoldInHands(pickup)) return;
        if (!pickup.TryPickUp(items.HeldItemAnchor))
            items.RegisterItem(PlayerItemSlot.Hands, null);
    }

    private void TryWater()
    {
        if (!infiniteWater && water <= 0) return;
        if (farmingTarget && farmingTarget.TryWaterSelected())
        {
            if (!infiniteWater) water--;
            return;
        }
        if (TryUseOnCrop(crop => crop.TryWater()) && !infiniteWater) water--;
    }

    private void UpdateFarmingPrompt()
    {
        if (!wateringView) return;
        string prompt = null;
        if (player.CanUseGameplayInput && farmingTarget)
        {
            farmingTarget.RefreshTarget();
            if (items.SelectedSlot == PlayerItemSlot.Hands && farmingTarget.CanCover)
                prompt = "[E]  흙덮기";
            else if (items.SelectedSlot == PlayerItemSlot.Hands && farmingTarget.CanHarvest)
                prompt = "[E]  감자 수확";
            else if (items.SelectedSlot == PlayerItemSlot.Hands && farmingTarget.CanPlant)
                prompt = "씨감자 심기 [E]";
            else if (items.SelectedSlot == PlayerItemSlot.WateringCan && farmingTarget.CanWater)
                prompt = "[ LMB ]  WATER";
        }
        wateringView.SetPrompt(prompt);
    }

    private void TryRefill()
    {
        if (!TryRaycast(out var hit)) return;
        var source = hit.collider.GetComponentInParent<WaterSource>();
        if (!source || !source.HasWater) return;
        water = wateringCanCapacity;
        Debug.Log($"[Watering Can] 물 채움 {water}/{wateringCanCapacity}", this);
    }

    private bool TryUseOnCrop(Func<PotatoGrowth, bool> action)
    {
        if (!TryRaycast(out var hit)) return false;
        var crop = hit.collider.GetComponentInParent<PotatoGrowth>();
        return crop && action(crop);
    }

    private bool TryRaycast(out RaycastHit hit)
    {
        hit = default;
        var camera = player.ViewCamera ? player.ViewCamera.GetComponent<Camera>() : Camera.main;
        return camera && Physics.Raycast(camera.ViewportPointToRay(new Vector3(0.5f, 0.5f)), out hit,
            actionRange, actionMask, QueryTriggerInteraction.Collide);
    }

    private void CreateToolVisuals()
    {
        toolInstances = new GameObject[toolPrefabs.Length];
        Transform anchor = items.HeldItemAnchor;
        if (!anchor) return;
        for (int index = 1; index < toolPrefabs.Length; index++)
        {
            if (!toolPrefabs[index]) continue;
            var instance = Instantiate(toolPrefabs[index], anchor, false);
            instance.name = toolPrefabs[index].name + " (Held)";
            instance.transform.localPosition = Vector3.zero;
            instance.SetActive(false);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            toolInstances[index] = instance;
        }
    }

    private void OnSlotChanged(PlayerItemSlot slot)
    {
        if (visuals) visuals.SetHandsVisible(slot == PlayerItemSlot.Hands);
        if (wateringView && slot != PlayerItemSlot.WateringCan) wateringView.SetVisible(false);
        if (toolInstances == null) return;
        for (int index = 1; index < toolInstances.Length; index++)
            if (toolInstances[index]) toolInstances[index].SetActive(index == (int)slot);
    }

    private void OnDestroy()
    {
        if (items != null) items.SelectedSlotChanged -= OnSlotChanged;
    }
}
