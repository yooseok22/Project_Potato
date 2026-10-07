using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(GroundGrid))]
public sealed class DugPotatoCleanupInteraction : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private string prompt = "정리하기 [ E ]";
    [SerializeField, Min(0.1f)] private float maximumDistance = 3f;

    private GroundGrid ground;

    public string Prompt => prompt;
    public bool RequiresHold => false;
    public float HoldDuration => 0f;

    private void Awake() => ground = GetComponent<GroundGrid>();

    public bool CanInteract(PlayerInteractionController player)
        => TryGetAimedDugCell(player, out _);

    public void Interact(PlayerInteractionController player)
    {
        if (TryGetAimedDugCell(player, out var cell)) ground.TryCleanBoarDugField(cell);
    }

    private bool TryGetAimedDugCell(PlayerInteractionController player, out Vector2Int cell)
    {
        cell = default;
        if (!ground || !player) return false;
        var controller = player.GetComponent<FirstPersonPlayerController>();
        var camera = controller && controller.ViewCamera
            ? controller.ViewCamera.GetComponent<Camera>() : Camera.main;
        if (!camera) return false;
        var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        return ground.TryGetCell(ray, maximumDistance, out cell, out _) && ground.IsBoarDugField(cell);
    }
}
