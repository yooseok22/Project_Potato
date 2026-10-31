using UnityEngine;

[DisallowMultipleComponent]
public sealed class StarchWaterBasket : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private PotatoStarchProcessor processor;
    [SerializeField] private ProcessingCarryItem carryItem;
    [SerializeField] private StarchSieve station;
    [SerializeField] private Transform interactionBone;
    [SerializeField] private Vector3 colliderBoneOffset;
    private BoxCollider interactionCollider;

    public ProcessingCarryItem CarryItem => carryItem;
    public bool RequiresHold => false;
    public float HoldDuration => 0f;
    public string Prompt => !processor ? string.Empty : processor.WaterBasketState switch
    {
        StarchWaterBasketState.Docked => station ? station.Prompt : string.Empty,
        StarchWaterBasketState.Lowered => processor.CanTakeWaterBasket
            ? "[E] 물 바스켓 들기" : "위 틀의 전분을 모두 포장한 뒤 바스켓을 드세요",
        _ => "물 바스켓을 든 채 손잡이에 [E]",
    };

    private void Awake()
    {
        if (!processor) processor = GetComponentInParent<PotatoStarchProcessor>();
        if (!carryItem) carryItem = GetComponent<ProcessingCarryItem>();
        if (!station) station = GetComponentInParent<StarchSieve>();
        interactionCollider = GetComponent<BoxCollider>();
    }

    public void Configure(PotatoStarchProcessor value)
    {
        processor = value;
        if (!carryItem) carryItem = GetComponent<ProcessingCarryItem>();
        if (!station) station = GetComponentInParent<StarchSieve>();
    }

    public void ConfigureCollider(Transform bone)
    {
        interactionBone = bone;
        interactionCollider = GetComponent<BoxCollider>();
        colliderBoneOffset = bone.InverseTransformPoint(interactionCollider.bounds.center);
    }

    // Only the collider follows the animated basket; the carry root stays at its recorded home.
    public void RefreshCollider()
    {
        if (!interactionCollider) interactionCollider = GetComponent<BoxCollider>();
        if (interactionBone && interactionCollider && carryItem && !carryItem.IsCarried)
            interactionCollider.center = transform.InverseTransformPoint(interactionBone.TransformPoint(colliderBoneOffset));
    }
    private void LateUpdate() => RefreshCollider();

    public bool CanInteract(PlayerInteractionController player) => isActiveAndEnabled && processor &&
        (processor.CanTakeWaterBasket ||
         (processor.WaterBasketState == StarchWaterBasketState.Docked && station && station.CanInteract(player)));

    public void Interact(PlayerInteractionController player)
    {
        if (!CanInteract(player)) return;
        if (processor.CanTakeWaterBasket) processor.TryTakeWaterBasket(player);
        else station.Interact(player);
    }
}
