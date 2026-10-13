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
            return processor.SieveState switch
            {
                StarchSieveState.Filling => processor.BlenderState == StarchBlenderState.CarriedFull
                    ? $"[E] 간 감자 투입 ({processor.SieveFillCount}/5)" : $"빨간 통 ({processor.SieveFillCount}/5)",
                StarchSieveState.FillingSegment => $"빨간 통 채우는 중 ({processor.SieveFillCount}/5)",
                StarchSieveState.Draining => "전분 배출 · 아래 틀 채우는 중",
                StarchSieveState.WetStarchReady => "아래 전분 틀에 [E]로 틀을 꺼내세요",
                StarchSieveState.TrayRemoved => "[E] 첫 번째 손잡이 당기기",
                StarchSieveState.HandleOneMoving or StarchSieveState.HandleTwoMoving => "손잡이 조작 중",
                StarchSieveState.HandleOneDone => "[E] 두 번째 손잡이 당기기",
                StarchSieveState.Dropping => "빨간 통 내려오는 중",
                StarchSieveState.ReadyToDry => "[E] 재배기 위에 전분 틀 놓기",
                StarchSieveState.Drying => processor.TrayState == StarchTrayState.Dried
                    ? "전분 틀에 [E]로 전분을 수거하세요" : $"전분 틀 건조 중 {processor.DryingSecondsRemaining}초",
                _ => string.Empty
            };
        }
    }
    private void Awake() { if (!processor) processor = GetComponentInParent<PotatoStarchProcessor>(); }
    public void Configure(PotatoStarchProcessor value) => processor = value;
    public bool CanInteract(PlayerInteractionController player) => enabled && gameObject.activeInHierarchy && processor &&
        (processor.SieveState == StarchSieveState.Filling || processor.SieveState == StarchSieveState.TrayRemoved ||
         processor.SieveState == StarchSieveState.HandleOneDone || processor.SieveState == StarchSieveState.ReadyToDry);
    public void Interact(PlayerInteractionController player)
    {
        if (!processor) return;
        if (processor.SieveState == StarchSieveState.Filling) processor.TryPourBatch(player);
        else processor.TryUseMaker(player);
    }
}
