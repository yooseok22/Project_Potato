using UnityEngine;

[DefaultExecutionOrder(200)]
public sealed class FarmingTargetSelector : MonoBehaviour
{
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform player;
    [SerializeField] private GroundGrid ground;
    [SerializeField] private FarmingPreviewView preview;
    [SerializeField, Min(0f)] private float interactionRange = 4f;
    [SerializeField, Min(1f)] private float rayDistance = 100f;
    [Tooltip("Only obstacle layers. Exclude the ground, tile visuals and player.")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private bool enableDigging = true;
    public bool HasTarget { get; private set; }
    public bool CanFarm { get; private set; }
    public bool CanFill { get; private set; }
    public bool CanWater { get; private set; }
    public bool CanPlant { get; private set; }
    public Vector2Int SelectedCell { get; private set; }
    public GroundGrid Ground => ground;

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
            HasTarget = CanFarm = CanFill = CanWater = CanPlant = false;
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
        if (!HasTarget || !CanReachSelected() || !ground.CanWater(SelectedCell)) return false;
        bool changed = ground.TryWater(SelectedCell);
        RefreshTarget();
        return changed;
    }
    public bool TryPlantSelected(PotatoFarmingSystem farmingSystem)
    {
        RefreshTarget();
        if (!farmingSystem || !HasTarget || !CanReachSelected() || !ground.CanPlant(SelectedCell)) return false;
        Vector2Int cell = SelectedCell;
        if (!ground.TrySetCellState(cell, GroundCellState.PotatoCovered)) return false;
        bool changed = farmingSystem.TryStartFarming(ground, cell);
        if (!changed) ground.TrySetCellState(cell, GroundCellState.PlantingHole);
        else Debug.Log(
            $"[Farming Handoff] Ground='{ground.name}', Cell={cell}, State={GroundCellState.PotatoCovered}, Tile=017 -> PotatoFarmingSystem",
            farmingSystem);
        RefreshTarget();
        return changed;
    }
    public void RefreshTarget()
    {
        HasTarget = CanFarm = CanFill = CanWater = CanPlant = false;
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
        CanFarm = ground.CanUseHoeSecondary(cell) && inRange;
        CanFill = ground.CanUseHoePrimary(cell) && inRange;
        CanWater = ground.CanWater(cell) && inRange;
        CanPlant = ground.CanPlant(cell) && inRange;
        var items = player.GetComponent<PlayerItemController>();
        if (items && items.SelectedSlot == PlayerItemSlot.Hoe)
            preview.Show(center, ground.CellSize, CanFarm || CanFill);
        else
            preview.Hide();
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
    private void OnDisable() { HasTarget = CanFarm = CanFill = CanWater = CanPlant = false; if (preview) preview.Hide(); }
}
