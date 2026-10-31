using UnityEngine;

[DisallowMultipleComponent]
public sealed class StarchDryingTray : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private PotatoStarchProcessor processor;
    [SerializeField] private ProcessingCarryItem carryItem;

    public ProcessingCarryItem CarryItem => carryItem;
    public bool RequiresHold => false;
    public float HoldDuration => 0f;
    public string Prompt => processor == null ? string.Empty : processor.TrayState switch
    {
        StarchTrayState.InstalledEmpty => "건조 틀 비어 있음",
        StarchTrayState.InstalledWet => "[E] 건조 틀 꺼내기",
        StarchTrayState.Drying => $"건조 중 {processor.DryingSecondsRemaining}초",
        StarchTrayState.EmptyOnTop => processor.SieveState switch
        {
            StarchSieveState.ReadyToPack => "전분 소진 · 아래 바스켓을 들고 가공기에 [E]",
            StarchSieveState.WaitingForBasketReturn => "가공기에 다시 [E]로 설비 초기화",
            StarchSieveState.DryBasketCarried => "바스켓을 든 채 가공기에 [E]로 빈 틀 내리기",
            _ => "빈 전분 틀 내려오는 중",
        },
        StarchTrayState.ReadyToPack => $"[E] 포장지를 들고 포장 ({processor.PackagingDuration:0.#}초 · 남은 {processor.RemainingPackCount}팩)",
        StarchTrayState.Packing => $"포장 중 {processor.PackagingSecondsRemaining:0.0}초",
        _ => string.Empty
    };

    private void Awake()
    {
        if (!processor) processor = GetComponentInParent<PotatoStarchProcessor>();
        if (!carryItem) carryItem = GetComponent<ProcessingCarryItem>();
    }

    public void Configure(PotatoStarchProcessor value)
    {
        processor = value;
        if (!carryItem) carryItem = GetComponent<ProcessingCarryItem>();
    }

    public bool CanInteract(PlayerInteractionController player) => enabled && gameObject.activeInHierarchy && processor &&
        (processor.TrayState == StarchTrayState.InstalledWet ||
         (processor.TrayState == StarchTrayState.ReadyToPack && processor.SieveState == StarchSieveState.ReadyToPack));

    public void Interact(PlayerInteractionController player)
    {
        if (!CanInteract(player)) return;
        if (processor.TrayState == StarchTrayState.ReadyToPack) processor.TryStartPackaging(player);
        else processor.TryTakeWetTray(player);
    }
}
