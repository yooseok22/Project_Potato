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
        StarchBlenderState.DockedEmpty => "[E] 바구니에서 가공할 감자 선택",
        StarchBlenderState.AwaitingPotatoes => $"[E] 감자 추가 투입 ({processor.LoadedPotatoCount}/{processor.PotatoesPerBatch})",
        StarchBlenderState.LidOpening => "뚜껑 여는 중",
        StarchBlenderState.LidClosing => "감자 투입 · 뚜껑 닫는 중",
        StarchBlenderState.LoadedReady => "[E] 믹서기 가동",
        StarchBlenderState.Starting => "믹서기 가동 시작",
        StarchBlenderState.Working => "감자 분쇄 중",
        StarchBlenderState.Ending => "믹서기 가동 종료 중",
        StarchBlenderState.GroundReady => "[E] 믹서기 통·뚜껑 들기",
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

    public bool CanInteract(PlayerInteractionController player) => enabled && gameObject.activeInHierarchy && processor &&
        (processor.BlenderState == StarchBlenderState.DockedEmpty || processor.BlenderState == StarchBlenderState.AwaitingPotatoes || processor.BlenderState == StarchBlenderState.LoadedReady ||
         processor.BlenderState == StarchBlenderState.GroundReady || processor.BlenderState == StarchBlenderState.CarriedEmpty);

    public void Interact(PlayerInteractionController player)
    {
        if (!processor) return;
        switch (processor.BlenderState)
        {
            case StarchBlenderState.DockedEmpty:
            case StarchBlenderState.AwaitingPotatoes:
                player?.GetComponent<BasketProcessingController>()?.TryOpen(processor);
                break;
            case StarchBlenderState.LoadedReady: processor.TryStartGrinding(); break;
            case StarchBlenderState.GroundReady: processor.TryTakeBlender(player); break;
            case StarchBlenderState.CarriedEmpty: processor.TryReturnBlender(player); break;
            case StarchBlenderState.CarriedFull: processor.TryReturnBlender(player); break;
        }
    }
}
