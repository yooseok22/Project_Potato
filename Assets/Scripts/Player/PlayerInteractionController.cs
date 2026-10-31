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
[DefaultExecutionOrder(-50)]
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
    private RaycastHit[] targetHits = new RaycastHit[16];
    private AutomationPlayerController automation;

    public string CurrentPrompt => current?.Prompt ?? string.Empty;
    public IPlayerInteractable CurrentTarget => current;
    public bool HasInteractionTarget => current != null;
    public float HoldProgress => current != null && current.RequiresHold
        ? Mathf.Clamp01(holdTime / Mathf.Max(0.01f, current.HoldDuration)) : 0f;
    public bool IsHoldingInteraction { get; private set; }
    public BoxPackingContent SelectedPackingContent { get; private set; }
    public bool ConsumesPackingSelectionInput => current is PotatoPackingBox;
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
        if (!automation) automation = GetComponent<AutomationPlayerController>();
        if (automation && automation.ConsumesGameplayActions)
        {
            CancelHold();
            highlighter?.SetHighlighted(false);
            if (current != null) { current = null; highlighter = null; TargetChanged?.Invoke(); }
            return;
        }
        if (!aimCamera) aimCamera = player.ViewCamera ? player.ViewCamera.GetComponent<Camera>() : Camera.main;
        RefreshTarget();

        var keyboard = Keyboard.current;
        if (player.CanUseGameplayInput && keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                SelectPackingContent(BoxPackingContent.Potato);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                SelectPackingContent(BoxPackingContent.Starch);
        }
        if (current is PotatoPackingBox box) box.SetPreviewSelection(SelectedPackingContent);
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

    public void SelectPackingContent(BoxPackingContent content)
    {
        if (content != BoxPackingContent.None && content != BoxPackingContent.Potato && content != BoxPackingContent.Starch)
            throw new ArgumentOutOfRangeException(nameof(content));
        SelectedPackingContent = content;
        if (current is PotatoPackingBox box) box.SetPreviewSelection(content);
    }

    private void RefreshTarget()
    {
        IPlayerInteractable found = null;
        IInteractionHighlight foundHighlight = null;
        if (aimCamera && player.HasGameplayFocus && TryRaycastTarget(out var hit))
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

    private bool TryRaycastTarget(out RaycastHit closest)
    {
        var ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        int count;
        // The near-plane ray can enter our CharacterController when looking down.
        // Collect hits before excluding self so external targets remain reachable.
        while (true)
        {
            count = Physics.RaycastNonAlloc(ray, targetHits, range, interactionMask,
                QueryTriggerInteraction.Collide);
            if (count < targetHits.Length) break;
            Array.Resize(ref targetHits, targetHits.Length * 2);
        }

        closest = default;
        float distance = float.PositiveInfinity;
        bool found = false;
        for (int index = 0; index < count; index++)
        {
            var hit = targetHits[index];
            if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
            closest = hit;
            distance = hit.distance;
            found = true;
        }
        return found;
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
