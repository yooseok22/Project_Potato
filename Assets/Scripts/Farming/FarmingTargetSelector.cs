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
    [SerializeField] private LayerMask rayMask = Physics.DefaultRaycastLayers;
    public bool HasTarget { get; private set; }
    public bool CanFarm { get; private set; }
    public Vector2Int SelectedCell { get; private set; }

    public void Configure(Camera camera, Transform target, GroundGrid grid, FarmingPreviewView view, LayerMask mask)
    {
        aimCamera = camera; player = target; ground = grid; preview = view; rayMask = mask;
    }

    private void LateUpdate() => RefreshTarget();

    public void RefreshTarget()
    {
        HasTarget = CanFarm = false;
        if (!aimCamera || !player || !ground || !preview) { if (preview) preview.Hide(); return; }
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, rayMask, QueryTriggerInteraction.Ignore)
            || hit.collider != ground.Surface)
        {
            preview.Hide();
            return;
        }
        HasTarget = true;
        SelectedCell = ground.WorldToCell(hit.point);
        Vector3 center = ground.CellCenter(SelectedCell);
        Vector3 delta = center - player.position;
        delta.y = 0f;
        CanFarm = ground.ContainsCell(SelectedCell) && delta.sqrMagnitude <= interactionRange * interactionRange;
        preview.Show(center, ground.CellSize, CanFarm);
    }

    private void OnDisable() { HasTarget = CanFarm = false; if (preview) preview.Hide(); }
}
