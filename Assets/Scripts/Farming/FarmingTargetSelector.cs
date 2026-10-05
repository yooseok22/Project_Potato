using UnityEngine;

[DefaultExecutionOrder(200)]
public sealed class FarmingTargetSelector : MonoBehaviour
{
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform player;
    [SerializeField] private GroundGrid ground;
    [SerializeField] private PotatoFarmingSystem farmingSystem;
    [SerializeField] private FarmingPreviewView preview;
    [SerializeField, Min(0f)] private float interactionRange = 4f;
    [SerializeField, Min(1f)] private float rayDistance = 100f;
    [Tooltip("Only obstacle layers. Exclude the ground, tile visuals and player.")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private bool enableDigging = true;
    private PotatoHarvestCoordinator harvestCoordinator;
    public bool HasTarget { get; private set; }
    public bool CanFarm { get; private set; }
    public bool CanFill { get; private set; }
    public bool CanWater { get; private set; }
    public bool CanPlant { get; private set; }
    public bool CanCover { get; private set; }
    public bool CanHarvest { get; private set; }
    public Vector2Int SelectedCell { get; private set; }
    public GroundGrid Ground => ground;

    private void Awake()
    {
        if (!farmingSystem) farmingSystem = FindAnyObjectByType<PotatoFarmingSystem>();
        harvestCoordinator = EnsureHarvestCoordinator(farmingSystem);

        var sicklePreview = GetComponent<SickleHarvestPreviewController>();
        if (!sicklePreview) sicklePreview = gameObject.AddComponent<SickleHarvestPreviewController>();
        sicklePreview.Configure(aimCamera, player, ground, harvestCoordinator, preview,
            interactionRange, rayDistance, obstacleMask);
    }

    public void Configure(Camera camera, Transform target, GroundGrid grid, FarmingPreviewView view, LayerMask mask)
    {
        aimCamera = camera; player = target; ground = grid; preview = view; obstacleMask = mask;
    }
    private void LateUpdate()
    {
        var items = player ? player.GetComponent<PlayerItemController>() : null;
        if (items && items.SelectedSlot != PlayerItemSlot.Hands &&
            items.SelectedSlot != PlayerItemSlot.Hoe && items.SelectedSlot != PlayerItemSlot.WateringCan)
        {
            HasTarget = CanFarm = CanFill = CanWater = CanPlant = CanCover = CanHarvest = false;
            if (preview) preview.Hide();
            return;
        }
        RefreshTarget();
    }
    public bool TryDigSelected()
    {
        RefreshTarget();
        return CanFarm && ground.TryDig(SelectedCell);
    }
    public bool TryFillSelected()
    {
        RefreshTarget();
        if (!CanFill || !ground.TryFill(SelectedCell)) return false;
        RefreshTarget();
        return true;
    }
    public bool TryUseHoePrimarySelected()
    {
        RefreshTarget();
        if (!enableDigging || !HasTarget || !CanReachSelected()) return false;
        bool changed = ground.TryUseHoePrimary(SelectedCell);
        RefreshTarget();
        return changed;
    }
    public bool TryUseHoeSecondarySelected()
    {
        RefreshTarget();
        if (!enableDigging || !HasTarget || !CanReachSelected()) return false;
        bool changed = ground.TryUseHoeSecondary(SelectedCell);
        RefreshTarget();
        return changed;
    }
    public bool TryWaterSelected()
    {
        RefreshTarget();
        if (!HasTarget || !CanWater || !CanReachSelected()) return false;
        Vector2Int cell = SelectedCell;
        if (!ground.TryWater(cell)) return false;

        GroundCellState state = ground.GetCellState(cell);
        bool handedOff = farmingSystem.TryWater(cell);
        if (handedOff)
            Debug.Log(
                $"[Farming Handoff] Ground='{ground.name}', Cell={cell}, State={state}, " +
                "Watered=True -> PotatoFarmingSystem.TryWater(cell)",
                farmingSystem);
        else
            Debug.LogWarning(
                $"[Farming Handoff] Water applied but PotatoFarmingSystem rejected Cell={cell}. " +
                "Check its Ground and Clock assignments.",
                farmingSystem);

        RefreshTarget();
        return true;
    }
    public bool TryPlantSelected(PotatoFarmingSystem farmingSystem)
    {
        RefreshTarget();
        if (!farmingSystem || !HasTarget || !CanPlant || !CanReachSelected()) return false;
        Vector2Int cell = SelectedCell;
        bool changed = farmingSystem.TryStartFarming(ground, cell);
        if (changed)
        {
            EnsureHarvestCoordinator(farmingSystem)?.ResetCell(ground, cell);
            Debug.Log(
                $"[Farming Handoff] Ground='{ground.name}', Cell={cell}, State={GroundCellState.PotatoCovered}, Tile=G_002 -> PotatoFarmingSystem",
                farmingSystem);
        }
        RefreshTarget();
        return changed;
    }
    public bool TryUseHandsSelected(PotatoFarmingSystem system)
    {
        RefreshTarget();
        if (!HasTarget || !CanReachSelected()) return false;
        Vector2Int cell = SelectedCell;
        bool changed;
        if (CanCover)
        {
            changed = ground.TryCoverPotato(cell);
            if (changed) harvestCoordinator?.MarkCovered(ground, cell);
        }
        else if (CanHarvest)
        {
            changed = harvestCoordinator && harvestCoordinator.TryHarvest(ground, cell, out _);
        }
        else
        {
            changed = TryPlantSelected(system);
        }
        RefreshTarget();
        return changed;
    }
    public bool TryUseSickleSelected(PotatoFarmingSystem system)
    {
        RefreshTarget();
        if (!HasTarget || !CanReachSelected()) return false;
        var coordinator = EnsureHarvestCoordinator(system);
        if (!coordinator || !coordinator.TryCutForHarvest(ground, SelectedCell)) return false;
        RefreshTarget();
        return true;
    }
    public bool TryUsePotatoForkSelected()
    {
        RefreshTarget();
        if (!HasTarget || !CanReachSelected()) return false;
        bool changed = harvestCoordinator
            ? harvestCoordinator.TryUsePotatoFork(ground, SelectedCell)
            : ground.TryUsePotatoFork(SelectedCell);
        RefreshTarget();
        return changed;
    }
    public void RefreshTarget()
    {
        HasTarget = CanFarm = CanFill = CanWater = CanPlant = CanCover = CanHarvest = false;
        if (!aimCamera || !player || !ground || !preview) { if (preview) preview.Hide(); return; }
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!ground.TryGetCell(ray, rayDistance, out Vector2Int cell, out float distance)
            || IsOccluded(ray, distance))
        {
            preview.Hide();
            return;
        }
        SelectedCell = cell;
        HasTarget = true;
        Vector3 center = ground.CellCenter(cell);
        Vector3 delta = center - player.position;
        delta.y = 0f;
        bool inRange = delta.sqrMagnitude <= interactionRange * interactionRange;
        var items = player.GetComponent<PlayerItemController>();
        var tools = player.GetComponent<PlayerToolController>();
        bool emptyHands = items && items.SelectedSlot == PlayerItemSlot.Hands && !items.HasItem(PlayerItemSlot.Hands);
        bool hasWater = !tools || tools.InfiniteWater || tools.Water > 0;
        CanFarm = ground.CanUseHoeSecondary(cell) && inRange;
        CanFill = ground.CanUseHoePrimary(cell) && inRange;
        CanWater = ground.CanWater(cell) && farmingSystem &&
            farmingSystem.TryGetFarmingTile(ground, cell, out var farmingTile) && !farmingTile.IsWatered &&
            inRange && hasWater;
        CanPlant = ground.CanPlant(cell) && inRange && emptyHands;
        CanCover = harvestCoordinator && ground.CanCoverPotato(cell) && inRange && emptyHands;
        CanHarvest = harvestCoordinator && harvestCoordinator.CanHarvest(ground, cell)
            && inRange && emptyHands;
        if (!items) { preview.Hide(); return; }
        switch (items.SelectedSlot)
        {
            case PlayerItemSlot.Hands:
                preview.Show(center, ground.CellSize, CanPlant || CanCover || CanHarvest);
                break;
            case PlayerItemSlot.Hoe:
                preview.Show(center, ground.CellSize, CanFarm || CanFill);
                break;
            case PlayerItemSlot.WateringCan:
                preview.Show(center, ground.CellSize, CanWater);
                break;
            default:
                preview.Hide();
                break;
        }
    }
    private bool CanReachSelected()
    {
        if (!ground || !player || !ground.ContainsCell(SelectedCell)) return false;
        Vector3 delta = ground.CellCenter(SelectedCell) - player.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= interactionRange * interactionRange;
    }
    private bool IsOccluded(Ray ray, float distance)
    {
        foreach (var hit in Physics.RaycastAll(ray, Mathf.Max(0f, distance - 0.01f), obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(player) || hit.collider.GetComponentInParent<GroundGrid>() == ground
                || hit.collider.GetComponentInParent<GroundTileRenderer>() != null) continue;
            return true;
        }
        return false;
    }
    private void OnDisable()
    {
        HasTarget = CanFarm = CanFill = CanWater = CanPlant = CanCover = CanHarvest = false;
        if (preview) preview.Hide();
    }

    private PotatoHarvestCoordinator EnsureHarvestCoordinator(PotatoFarmingSystem system)
    {
        if (!system) return null;
        if (!harvestCoordinator)
        {
            harvestCoordinator = system.GetComponent<PotatoHarvestCoordinator>();
            if (!harvestCoordinator) harvestCoordinator = system.gameObject.AddComponent<PotatoHarvestCoordinator>();
        }
        harvestCoordinator.Configure(system);
        return harvestCoordinator;
    }
}
