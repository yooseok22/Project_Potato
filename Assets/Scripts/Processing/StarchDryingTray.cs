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
        StarchTrayState.Dried => processor.SieveState switch
        {
            StarchSieveState.Drying => "아래 물 바스켓을 [E]로 먼저 들어 올리세요",
            StarchSieveState.DryBasketCarried => "물 바스켓을 든 채 손잡이에 [E]",
            _ => "건조 틀 내려오는 중",
        },
        StarchTrayState.ReadyToPack => processor.SieveState == StarchSieveState.WaitingForBasketReturn
            ? "가공기에 [E]로 물 바스켓을 먼저 복귀하세요"
            : $"[E] 맨손으로 포장 ({processor.PackagingDuration:0.#}초 · 포장지 1개 필요 · 남은 {processor.RemainingPackCount}팩)",
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
