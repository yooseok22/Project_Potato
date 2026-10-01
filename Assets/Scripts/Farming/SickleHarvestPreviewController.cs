using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Displays the existing farming tile preview while the local player holds the sickle.
/// Harvest state is queried without completing or otherwise mutating the crop.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(201)]
public sealed class SickleHarvestPreviewController : MonoBehaviour
{
    private Camera aimCamera;
    private Transform player;
    private GroundGrid ground;
    private PotatoFarmingSystem farmingSystem;
    private FarmingPreviewView preview;
    private float interactionRange;
    private float rayDistance;
    private LayerMask obstacleMask;

    public void Configure(Camera camera, Transform target, GroundGrid grid,
        PotatoFarmingSystem system, FarmingPreviewView previewView,
        float range, float maximumRayDistance, LayerMask obstacles)
    {
        aimCamera = camera;
        player = target;
        ground = grid;
        farmingSystem = system;
        preview = previewView;
        interactionRange = Mathf.Max(0f, range);
        rayDistance = Mathf.Max(1f, maximumRayDistance);
        obstacleMask = obstacles;
    }

    private void LateUpdate()
    {
        var items = player ? player.GetComponent<PlayerItemController>() : null;
        if (!items || items.SelectedSlot != PlayerItemSlot.Sickle) return;

        if (!aimCamera || !ground || !farmingSystem || !preview)
        {
            if (preview) preview.Hide();
            return;
        }

        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!ground.TryGetCell(ray, rayDistance, out Vector2Int cell, out float distance)
            || IsOccluded(ray, distance))
        {
            preview.Hide();
            return;
        }

        Vector3 center = ground.CellCenter(cell);
        Vector3 delta = center - player.position;
        delta.y = 0f;
        bool inRange = delta.sqrMagnitude <= interactionRange * interactionRange;
        bool canHarvest = farmingSystem.TryGetFarmingTile(ground, cell, out var farmingTile)
            && IsHarvestStage(farmingTile.Stage)
            && inRange;

        preview.Show(center, ground.CellSize, canHarvest);

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && inRange && farmingTile != null)
            Debug.Log(
                $"[Sickle Inspect] Cell={cell}, HP={farmingTile.CurrentHealth:0.#}/{farmingTile.MaxHealth:0.#}, " +
                $"State={HarvestStateName(farmingTile.Stage)}, GrowthStage={farmingTile.Stage}",
                farmingSystem);
    }

    public static bool IsHarvestStage(PotatoGrowthStage stage)
        => stage == PotatoGrowthStage.SixLargeLeaves
            || stage == PotatoGrowthStage.FreshSixLargeLeaves
            || stage == PotatoGrowthStage.WitheredSixLeaves;

    public static string HarvestStateName(PotatoGrowthStage stage)
    {
        return stage switch
        {
            PotatoGrowthStage.SixLargeLeaves => "이른 수확",
            PotatoGrowthStage.FreshSixLargeLeaves => "적기 수확",
            PotatoGrowthStage.WitheredSixLeaves => "늦은 수확",
            _ => "수확 불가"
        };
    }

    private bool IsOccluded(Ray ray, float distance)
    {
        foreach (var hit in Physics.RaycastAll(ray, Mathf.Max(0f, distance - 0.01f),
            obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(player)
                || hit.collider.GetComponentInParent<GroundGrid>() == ground
                || hit.collider.GetComponentInParent<GroundTileRenderer>() != null)
                continue;
            return true;
        }
        return false;
    }

    private void OnDisable()
    {
        var items = player ? player.GetComponent<PlayerItemController>() : null;
        if (items && items.SelectedSlot == PlayerItemSlot.Sickle && preview) preview.Hide();
    }
}
