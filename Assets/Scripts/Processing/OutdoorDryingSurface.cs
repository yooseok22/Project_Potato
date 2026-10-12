using UnityEngine;

[DisallowMultipleComponent]
public sealed class OutdoorDryingSurface : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private PotatoStarchProcessor processor;
    [SerializeField, Min(0f)] private float placementHeight = 0.08f;
    [SerializeField, Min(0.5f)] private float raycastDistance = 5f;

    public string Prompt => processor && processor.TrayState == StarchTrayState.CarriedWet
        ? "[E] 건조 틀 놓기" : string.Empty;
    public bool RequiresHold => false;
    public float HoldDuration => 0f;

    private void Awake()
    {
        if (!processor) processor = FindAnyObjectByType<PotatoStarchProcessor>();
    }

    public void Configure(PotatoStarchProcessor value) => processor = value;

    public bool CanInteract(PlayerInteractionController player)
        => enabled && gameObject.activeInHierarchy && processor &&
           processor.TrayState == StarchTrayState.CarriedWet;

    public void Interact(PlayerInteractionController player)
    {
        if (!processor || !player) return;
        var camera = player.GetComponent<FirstPersonPlayerController>()?.ViewCamera;
        var aimCamera = camera ? camera.GetComponent<Camera>() : Camera.main;
        if (!aimCamera || !Physics.Raycast(aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f)),
                out var hit, raycastDistance, Physics.AllLayers, QueryTriggerInteraction.Collide) ||
            hit.collider.GetComponentInParent<OutdoorDryingSurface>() != this)
        {
            processor.TryPlaceTray(player, Vector3.zero, Quaternion.identity, false);
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(player.transform.forward, hit.normal).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Quaternion rotation = Quaternion.LookRotation(forward, hit.normal);
        processor.TryPlaceTray(player, hit.point + hit.normal * placementHeight, rotation, true);
    }
}
