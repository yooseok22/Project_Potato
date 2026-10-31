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
                StarchSieveState.TrayRemoved => "[E] 양쪽 손잡이 당기기",
                StarchSieveState.HandlesMoving or StarchSieveState.DryHandlesMoving => "양쪽 손잡이 조작 중",
                StarchSieveState.Dropping => "빨간 통 내려오는 중",
                StarchSieveState.DryDropping => "건조 틀 내려오는 중",
                StarchSieveState.DryBasketCarried => "[E] 양쪽 손잡이 당겨 건조 틀 내리기",
                StarchSieveState.WaitingForBasketReturn => "[E] 바스켓 복귀 및 설비 초기화",
                StarchSieveState.ReadyToDry => "[E] 재배기 위에 전분 틀 놓기",
                StarchSieveState.Drying => $"전분 틀 건조 중 {processor.DryingSecondsRemaining}초",
                StarchSieveState.ReadyToPack => processor.CanTakeWaterBasket
                    ? "전분 소진 · 아래 바스켓을 들고 가공기에 [E]"
                    : processor.TrayState == StarchTrayState.Packing
                        ? $"포장 중 {processor.PackagingSecondsRemaining:0.0}초"
                        : $"[E] 포장지를 들고 위 전분 포장 · 남은 {processor.RemainingPackCount}팩",
                _ => string.Empty
            };
        }
    }
    private void Awake() { if (!processor) processor = GetComponentInParent<PotatoStarchProcessor>(); }
    public void Configure(PotatoStarchProcessor value) => processor = value;
    public bool CanInteract(PlayerInteractionController player) => enabled && gameObject.activeInHierarchy && processor &&
        (processor.SieveState == StarchSieveState.Filling || processor.SieveState == StarchSieveState.TrayRemoved ||
         processor.SieveState == StarchSieveState.ReadyToDry ||
         (processor.SieveState == StarchSieveState.ReadyToPack && processor.TrayState == StarchTrayState.ReadyToPack) ||
         processor.SieveState == StarchSieveState.DryBasketCarried ||
         processor.SieveState == StarchSieveState.WaitingForBasketReturn);
    public void Interact(PlayerInteractionController player)
    {
        if (!processor) return;
        if (processor.SieveState == StarchSieveState.Filling) processor.TryPourBatch(player);
        else if (processor.SieveState == StarchSieveState.ReadyToPack) processor.TryStartPackaging(player);
        else processor.TryUseMaker(player);
    }
}
