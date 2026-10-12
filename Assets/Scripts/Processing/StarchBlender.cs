using UnityEngine;

[DisallowMultipleComponent]
public sealed class StarchBlender : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private PotatoStarchProcessor processor;
    [SerializeField] private ProcessingCarryItem carryItem;

    public ProcessingCarryItem CarryItem => carryItem;
    public bool RequiresHold => false;
    public float HoldDuration => 0f;
    public string Prompt => processor == null ? string.Empty : processor.BlenderState switch
    {
        StarchBlenderState.DockedEmpty => "[E] 감자 4개 소비 후 분쇄 (테스트)",
        StarchBlenderState.Grinding => $"분쇄 중 {processor.GrindingSecondsRemaining}초",
        StarchBlenderState.GroundReady => "[E] 믹서기 들기",
        StarchBlenderState.CarriedFull => "갈린 감자를 체 통에 부어주세요",
        StarchBlenderState.CarriedEmpty => "[E] 빈 믹서기 원위치",
        _ => string.Empty
    };

    private void Awake()
    {
        if (!processor) processor = GetComponentInParent<PotatoStarchProcessor>();
        if (!carryItem) carryItem = GetComponentInChildren<ProcessingCarryItem>(true);
    }

    public void Configure(PotatoStarchProcessor value)
    {
        processor = value;
        if (!carryItem) carryItem = GetComponentInChildren<ProcessingCarryItem>(true);
    }

    public bool CanInteract(PlayerInteractionController player) => enabled && gameObject.activeInHierarchy;

    public void Interact(PlayerInteractionController player)
    {
        if (!processor) return;
        switch (processor.BlenderState)
        {
            case StarchBlenderState.DockedEmpty: processor.TryStartGrinding(); break;
            case StarchBlenderState.GroundReady: processor.TryTakeBlender(player); break;
            case StarchBlenderState.CarriedEmpty: processor.TryReturnBlender(player); break;
            case StarchBlenderState.Grinding: processor.TryTakeBlender(player); break;
            case StarchBlenderState.CarriedFull: processor.TryReturnBlender(player); break;
        }
    }
}
