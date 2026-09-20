using UnityEngine;
using UnityEngine.InputSystem;

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
    public Vector2Int SelectedCell { get; private set; }

    public void Configure(Camera camera, Transform target, GroundGrid grid, FarmingPreviewView view, LayerMask mask)
    {
        aimCamera = camera; player = target; ground = grid; preview = view; obstacleMask = mask;
    }
    private void LateUpdate()
    {
        RefreshTarget();
        if (!enableDigging || !Application.isFocused || Cursor.lockState != CursorLockMode.Locked
            || Mouse.current == null) return;
        var controller = player ? player.GetComponent<ThirdPersonPlayerController>() : null;
        // The click used to return from Escape only captures the cursor.
        if (controller && controller.CursorCapturedThisFrame) return;
        if (Mouse.current.leftButton.wasPressedThisFrame) TryFillSelected();
        else if (Mouse.current.rightButton.wasPressedThisFrame) TryDigSelected();
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
    public void RefreshTarget()
    {
        HasTarget = CanFarm = CanFill = false;
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
        CanFarm = ground.CanDig(cell) && inRange;
        CanFill = ground.CanFill(cell) && inRange;
        preview.Show(center, ground.CellSize, CanFarm || CanFill);
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
    private void OnDisable() { HasTarget = CanFarm = CanFill = false; if (preview) preview.Hide(); }
}
