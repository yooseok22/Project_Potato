using UnityEngine;
using UnityEngine.Events;

public sealed class InteractionTarget : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private string prompt = "Interact";
    [SerializeField] private bool requiresHold;
    [SerializeField, Min(0.05f)] private float holdDuration = 0.5f;
    [SerializeField] private UnityEvent onInteract;

    public string Prompt => prompt;
    public bool RequiresHold => requiresHold;
    public float HoldDuration => holdDuration;
    public bool CanInteract(PlayerInteractionController player) => enabled && gameObject.activeInHierarchy;
    public void Interact(PlayerInteractionController player) => onInteract?.Invoke();
}
