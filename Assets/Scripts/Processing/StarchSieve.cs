using UnityEngine;

[DisallowMultipleComponent]
public sealed class StarchSieve : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private PotatoStarchProcessor processor;

    public bool RequiresHold => false;
    public float HoldDuration => 0f;
    public string Prompt
    {
        get
        {
            if (!processor) return string.Empty;
            if (processor.BlenderState == StarchBlenderState.CarriedFull)
                return $"[E] 간 감자 붓기 ({processor.SieveFillCount}/{processor.SieveBatchCapacity})";
            if (processor.SieveState == StarchSieveState.Draining)
                return $"전분 분리 중 {processor.DrainingSecondsRemaining}초";
            if (processor.SieveState == StarchSieveState.WetStarchReady &&
                processor.TrayState == StarchTrayState.InstalledWet)
                return "[E] 건조 틀 꺼내기";
            if (processor.SieveState == StarchSieveState.TrayRemoved)
                return "건조 틀 사용 중";
            return $"체 통 ({processor.SieveFillCount}/{processor.SieveBatchCapacity})";
        }
    }

    private void Awake()
    {
        if (!processor) processor = GetComponentInParent<PotatoStarchProcessor>();
    }

    public void Configure(PotatoStarchProcessor value) => processor = value;
    public bool CanInteract(PlayerInteractionController player) => enabled && gameObject.activeInHierarchy;

    public void Interact(PlayerInteractionController player)
    {
        if (!processor) return;

        // The sieve body can occlude the installed tray's collider. Once draining
        // finishes, route the sieve interaction to the tray pickup operation.
        if (processor.SieveState == StarchSieveState.WetStarchReady &&
            processor.TrayState == StarchTrayState.InstalledWet)
        {
            processor.TryTakeWetTray(player);
            return;
        }

        processor.TryPourBatch(player);
    }
}
