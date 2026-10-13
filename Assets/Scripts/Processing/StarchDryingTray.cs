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
        StarchTrayState.Dried => "[E] 전분 수거 (테스트)",
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
        (processor.TrayState == StarchTrayState.InstalledWet || processor.TrayState == StarchTrayState.Dried);

    public void Interact(PlayerInteractionController player)
    {
        if (!processor) return;
        if (processor.TrayState == StarchTrayState.Dried) processor.TryCollectProduct();
        else processor.TryTakeWetTray(player);
    }
}
