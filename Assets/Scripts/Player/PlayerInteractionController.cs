using System;
using UnityEngine;
using UnityEngine.InputSystem;

public interface IPlayerInteractable
{
    string Prompt { get; }
    bool RequiresHold { get; }
    float HoldDuration { get; }
    bool CanInteract(PlayerInteractionController player);
    void Interact(PlayerInteractionController player);
}

public interface IInteractionHighlight
{
    void SetHighlighted(bool highlighted);
}

[RequireComponent(typeof(PlayerActionBlocker), typeof(FirstPersonPlayerController))]
public sealed class PlayerInteractionController : MonoBehaviour
{
    [SerializeField] private Camera aimCamera;
    [SerializeField, Min(0.1f)] private float range = 3f;
    [SerializeField] private LayerMask interactionMask = ~0;

    private FirstPersonPlayerController player;
    private PlayerActionBlocker blocker;
    private IPlayerInteractable current;
    private IInteractionHighlight highlighter;
    private float holdTime;

    public string CurrentPrompt => current?.Prompt ?? string.Empty;
    public float HoldProgress => current != null && current.RequiresHold
        ? Mathf.Clamp01(holdTime / Mathf.Max(0.01f, current.HoldDuration)) : 0f;
    public bool IsHoldingInteraction { get; private set; }
    public event Action TargetChanged;
    public event Action HoldProgressChanged;

    private void Awake()
    {
        player = GetComponent<FirstPersonPlayerController>();
        blocker = GetComponent<PlayerActionBlocker>();
    }

    private void Update()
    {
        if (!player.IsLocallyControlled) return;
        if (!aimCamera) aimCamera = player.ViewCamera ? player.ViewCamera.GetComponent<Camera>() : Camera.main;
        RefreshTarget();

        var keyboard = Keyboard.current;
        if (!player.CanUseGameplayInput || keyboard == null || current == null || !current.CanInteract(this))
        {
            CancelHold();
            return;
        }

        if (!current.RequiresHold)
        {
            if (keyboard.eKey.wasPressedThisFrame) current.Interact(this);
            return;
        }
        if (keyboard.eKey.wasPressedThisFrame) BeginHold();
        if (!IsHoldingInteraction) return;
        if (!keyboard.eKey.isPressed) { CancelHold(); return; }
        holdTime += Time.deltaTime;
        HoldProgressChanged?.Invoke();
        if (holdTime < Mathf.Max(0.01f, current.HoldDuration)) return;
        var completed = current;
        CancelHold();
        if (completed.CanInteract(this)) completed.Interact(this);
    }

    private void RefreshTarget()
    {
        IPlayerInteractable found = null;
        IInteractionHighlight foundHighlight = null;
        if (aimCamera && player.HasGameplayFocus &&
            Physics.Raycast(aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f)), out var hit,
                range, interactionMask, QueryTriggerInteraction.Collide))
        {
            foreach (var behaviour in hit.collider.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (found == null && behaviour is IPlayerInteractable interactable) found = interactable;
                if (foundHighlight == null && behaviour is IInteractionHighlight highlight) foundHighlight = highlight;
            }
        }
        if (ReferenceEquals(found, current)) return;
        CancelHold();
        highlighter?.SetHighlighted(false);
        current = found;
        highlighter = foundHighlight;
        highlighter?.SetHighlighted(current != null);
        TargetChanged?.Invoke();
    }

    private void BeginHold()
    {
        IsHoldingInteraction = true;
        holdTime = 0f;
        blocker.SetBlocked(PlayerActionBlockReason.InteractionHold, true);
        HoldProgressChanged?.Invoke();
    }

    private void CancelHold()
    {
        if (!IsHoldingInteraction && holdTime <= 0f) return;
        IsHoldingInteraction = false;
        holdTime = 0f;
        blocker.SetBlocked(PlayerActionBlockReason.InteractionHold, false);
        HoldProgressChanged?.Invoke();
    }

    private void OnDisable()
    {
        CancelHold();
        highlighter?.SetHighlighted(false);
        current = null;
        highlighter = null;
    }
}
