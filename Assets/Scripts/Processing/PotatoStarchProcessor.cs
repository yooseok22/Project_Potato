using System;
using UnityEngine;

public enum StarchBlenderState : byte
{
    DockedEmpty, LidOpening, LidClosing, LoadedReady, Starting, Working, Ending,
    GroundReady, CarriedFull, CarriedEmpty
}
public enum StarchSieveState : byte
{
    Filling, FillingSegment, Draining, WetStarchReady, TrayRemoved,
    HandlesMoving, Dropping, ReadyToDry, Drying, DryHandlesMoving, DryDropping, ReadyToPack,
    DryBasketCarried, WaitingForBasketReturn
}
public enum StarchTrayState : byte { InstalledEmpty, InstalledWet, CarriedWet, Drying, Dried, ReadyToPack, Packing, EmptyOnTop }
public enum StarchWaterBasketState : byte { Docked, Lowered, Carried }

[DisallowMultipleComponent]
public sealed class PotatoStarchProcessor : MonoBehaviour
{
    private const string Prefix = "[PotatoStarch] ";
    [SerializeField] private WorldClock clock;
    [SerializeField] private StarchBlender blender;
    [SerializeField] private StarchSieve sieve;
    [SerializeField] private StarchDryingTray dryingTray;
    [SerializeField] private StarchWaterBasket waterBasket;
    [SerializeField] private StarchPropAnimationDriver blenderAnimation;
    [SerializeField] private StarchPropAnimationDriver makerAnimation;
    [SerializeField] private Transform dryingAnchor;
    [SerializeField, Min(0.1f)] private float dryingSeconds = 150f;
    [SerializeField, Min(0.1f)] private float dryingLogIntervalSeconds = 30f;
    [SerializeField, Min(1)] private int potatoesPerBatch = 4;
    [SerializeField, Min(1)] private int starchPacksPerCycle = 5;
    [SerializeField, Min(0.1f)] private float packagingSeconds = 3f;
    private const int Capacity = 5;
    private WorldClock boundClock;
    private double fallbackTime, blenderStartedAt, makerStartedAt, dryingEndsAt, nextDryingLogAt;
    private bool initialized;
    private double packagingEndsAt;
    private PlayerInteractionController packagingPlayer;
    private StarchPackagingInventory packagingInventory;

    public StarchBlenderState BlenderState { get; private set; }
    public StarchSieveState SieveState { get; private set; }
    public StarchTrayState TrayState { get; private set; }
    public StarchWaterBasketState WaterBasketState { get; private set; }
    public bool CanTakeWaterBasket => WaterBasketState == StarchWaterBasketState.Lowered &&
        TrayState == StarchTrayState.EmptyOnTop && SieveState == StarchSieveState.ReadyToPack && RemainingPackCount == 0;
    public int SieveFillCount { get; private set; }
    public int SieveBatchCapacity => Capacity;
    public int LastProducedPackCount { get; private set; }
    public int TotalProducedPackCount { get; private set; }
    public int RemainingPackCount { get; private set; }
    public float PackagingDuration => packagingSeconds;
    public float PackagingSecondsRemaining => (float)Math.Max(0d, packagingEndsAt - Now);
    public double HandlesDuration => Math.Max(MakerLength(StarchAnimationId.HandleOne), MakerLength(StarchAnimationId.HandleTwo));
    public int DryingSecondsRemaining => (int)Math.Ceiling(Math.Max(0d, dryingEndsAt - Now));
    public double GrindingDuration => BlenderLength(StarchAnimationId.BlenderStart) +
        BlenderLength(StarchAnimationId.BlenderWorking) + BlenderLength(StarchAnimationId.BlenderEnd);
    public double DrainingDuration => Math.Max(MakerLength(StarchAnimationId.BasketWorking),
        MakerLength(StarchAnimationId.TrayFill));
    public float DryingDuration => dryingSeconds;
    public StarchPropAnimationDriver BlenderAnimation => blenderAnimation;
    public StarchPropAnimationDriver MakerAnimation => makerAnimation;
    public Transform DryingAnchor => dryingAnchor;
    private double Now => clock ? clock.ElapsedSeconds : fallbackTime;

    private void Awake() { ResolveReferences(); BindClock(); }
    private void OnEnable() => BindClock();
    private void Start()
    {
        EnsureInitialized();
        Log($"설비 준비. 분쇄={GrindingDuration:0.###}초, 전분 배출={DrainingDuration:0.###}초, 건조={dryingSeconds:0.#}초");
    }
    private void Update()
    {
        // Check even when the shared clock is paused, so changing tools cannot finish a reserved pack.
        if (TrayState == StarchTrayState.Packing && !CanContinuePackaging()) CancelPackaging();
        if (clock) return;
        fallbackTime += Time.deltaTime;
        EvaluateTimers();
    }
    private void OnDisable()
    {
        CancelPackaging();
        if (boundClock) boundClock.TimeAdvanced -= EvaluateTimers;
        boundClock = null;
    }
    public void Configure(WorldClock worldClock, StarchBlender starchBlender, StarchSieve starchSieve,
        StarchDryingTray tray, StarchPropAnimationDriver blenderDriver,
        StarchPropAnimationDriver makerDriver, Transform topAnchor, StarchWaterBasket basket)
    {
        if (boundClock) boundClock.TimeAdvanced -= EvaluateTimers;
        boundClock = null;
        clock = worldClock;
        blender = starchBlender;
        sieve = starchSieve;
        dryingTray = tray;
        waterBasket = basket;
        blenderAnimation = blenderDriver;
        makerAnimation = makerDriver;
        dryingAnchor = topAnchor;
        ResolveReferences();
        BindClock();
    }
    private void ResolveReferences()
    {
        if (!clock) clock = FindAnyObjectByType<WorldClock>();
        blender?.Configure(this);
        sieve?.Configure(this);
        dryingTray?.Configure(this);
        waterBasket?.Configure(this);
    }
    private void BindClock()
    {
        if (boundClock == clock) return;
        if (boundClock) boundClock.TimeAdvanced -= EvaluateTimers;
        boundClock = clock;
        if (boundClock) boundClock.TimeAdvanced += EvaluateTimers;
    }
    private void EnsureInitialized()
    {
        if (initialized) return;
        if (!blenderAnimation || !makerAnimation || !dryingAnchor || !waterBasket)
            throw new InvalidOperationException("Starch props need animation drivers, a top drying anchor and a water basket.");
        ResetCycle();
        initialized = true;
    }
    private double BlenderLength(StarchAnimationId id) => blenderAnimation.Length(id);
    private double MakerLength(StarchAnimationId id) => makerAnimation.Length(id);

    public bool TryLoadBlender()
    {
        EnsureInitialized();
        if (BlenderState != StarchBlenderState.DockedEmpty || SieveState != StarchSieveState.Filling)
            return Fail("감자 투입 실패: 설비가 준비되지 않음");
        BlenderState = StarchBlenderState.LidOpening;
        blenderStartedAt = Now;
        blenderAnimation.SetTime(StarchAnimationId.BlenderOpen, 0d);
        Log("믹서기 뚜껑 열기 시작");
        return true;
    }
    public bool TryStartGrinding()
    {
        EnsureInitialized();
        if (BlenderState != StarchBlenderState.LoadedReady || SieveState != StarchSieveState.Filling)
            return Fail("믹서기 가동 실패: 감자 투입과 뚜껑 닫기가 완료되어야 함");
        BlenderState = StarchBlenderState.Starting;
        blenderStartedAt = Now;
        blenderAnimation.SetTime(StarchAnimationId.BlenderStart, 0d);
        Log("믹서기 가동 시작");
        return true;
    }
    public bool TryTakeBlender(PlayerInteractionController player)
    {
        if (BlenderState != StarchBlenderState.GroundReady) return Fail("믹서기 들기 실패: End가 완료되지 않음");
        if (player && (!blender || !blender.CarryItem || !blender.CarryItem.TryAttach(player)))
            return Fail("믹서기 들기 실패: 손 슬롯을 사용할 수 없음");
        BlenderState = StarchBlenderState.CarriedFull;
        Log("믹서기 통과 뚜껑 들기 완료");
        return true;
    }
    public bool TryPourBatch(PlayerInteractionController player)
    {
        if (BlenderState != StarchBlenderState.CarriedFull || SieveState != StarchSieveState.Filling || SieveFillCount >= Capacity)
            return Fail("재배기 투입 실패: 믹서기가 비었거나 재배기가 투입 대기 상태가 아님");
        if (player && !blender.CarryItem.IsCarriedBy(player)) return Fail("재배기 투입 실패: 자신의 믹서기를 들어야 함");
        SieveFillCount++;
        BlenderState = StarchBlenderState.CarriedEmpty;
        SieveState = StarchSieveState.FillingSegment;
        makerStartedAt = Now;
        makerAnimation.SetTime(StarchAnimationId.BasketFill, MakerLength(StarchAnimationId.BasketFill) * (SieveFillCount - 1) / Capacity);
        Log($"갈린 감자 투입 완료. 용량={SieveFillCount}/{Capacity}");
        return true;
    }
    public bool TryReturnBlender(PlayerInteractionController player)
    {
        if (BlenderState != StarchBlenderState.CarriedEmpty) return Fail("믹서기 복귀 실패: 먼저 내용물을 재배기에 투입해야 함");
        if (player && !blender.CarryItem.TryReturnHome(player)) return Fail("믹서기 복귀 실패: 손의 믹서기를 확인할 수 없음");
        blenderAnimation.ResetPose();
        BlenderState = StarchBlenderState.DockedEmpty;
        Log("빈 믹서기 통·뚜껑 원위치 및 초기 자세 복원");
        return true;
    }
    public bool TryTakeWetTray(PlayerInteractionController player)
    {
        if (SieveState != StarchSieveState.WetStarchReady || TrayState != StarchTrayState.InstalledWet)
            return Fail("건조 틀 들기 실패: 전분 배출이 완료되지 않음");
        if (BlenderState != StarchBlenderState.DockedEmpty) return Fail("건조 틀 들기 실패: 빈 믹서기를 먼저 복귀해야 함");
        if (player && !dryingTray.CarryItem.TryAttach(player)) return Fail("건조 틀 들기 실패: 손 슬롯을 사용할 수 없음");
        SieveState = StarchSieveState.TrayRemoved;
        TrayState = StarchTrayState.CarriedWet;
        Log("전분이 가득 찬 아래 틀 들기 완료");
        return true;
    }
    public bool TryUseMaker(PlayerInteractionController player)
    {
        if (SieveState == StarchSieveState.WaitingForBasketReturn) return TryReturnWaterBasket(player);
        if (SieveState == StarchSieveState.DryBasketCarried && TrayState == StarchTrayState.EmptyOnTop)
        {
            if (!IsHoldingWaterBasket(player)) return Fail("손잡이 조작 실패: 자신의 물 바스켓을 손에 들어야 함");
            SieveState = StarchSieveState.DryHandlesMoving;
            BeginHandles();
            Log("빈 전분 틀 내리기 시작. H1-1 USE + H2-1 USE 동시 작동");
            return true;
        }
        if (CanTakeWaterBasket) return Fail("손잡이 조작 실패: 아래 물 바스켓을 먼저 들어야 함");
        if (TrayState != StarchTrayState.CarriedWet || (player && !dryingTray.CarryItem.IsCarriedBy(player)))
            return Fail("재배기 조작 실패: 전분 틀을 들어야 함");
        switch (SieveState)
        {
            case StarchSieveState.TrayRemoved:
                SieveState = StarchSieveState.HandlesMoving;
                BeginHandles();
                Log("H1-1 USE + H2-1 USE 동시 작동");
                return true;
            case StarchSieveState.ReadyToDry:
                return TryPlaceTrayOnMaker(player);
            default:
                return Fail("재배기 조작 실패: 현재 애니메이션이 끝나야 함");
        }
    }
    public bool TryTakeWaterBasket(PlayerInteractionController player)
    {
        if (!CanTakeWaterBasket) return Fail("물 바스켓 들기 실패: 위 틀의 전분을 모두 포장해야 함");
        if (!waterBasket.CarryItem.TryAttach(player)) return Fail("물 바스켓 들기 실패: 손 슬롯을 사용할 수 없음");
        WaterBasketState = StarchWaterBasketState.Carried;
        SieveState = StarchSieveState.DryBasketCarried;
        Log("아래 물 바스켓 들기 완료. 물 바스켓을 든 채 손잡이에 [E]로 건조 틀 배출");
        return true;
    }

    private bool IsHoldingWaterBasket(PlayerInteractionController player) =>
        player && waterBasket && waterBasket.CarryItem.IsCarriedBy(player) &&
        player.GetComponent<PlayerItemController>().SelectedSlot == PlayerItemSlot.Hands;

    public bool TryReturnWaterBasket(PlayerInteractionController player)
    {
        if (SieveState != StarchSieveState.WaitingForBasketReturn || WaterBasketState != StarchWaterBasketState.Carried)
            return Fail("물 바스켓 복귀 실패: 건조 틀 낙하가 먼저 완료되어야 함");
        if (!IsHoldingWaterBasket(player) || !waterBasket.CarryItem.TryReturnHome(player))
            return Fail("물 바스켓 복귀 실패: 자신의 물 바스켓을 손에 들어야 함");
        ResetCycle();
        Log("물 바스켓 복귀 및 빈 전분 틀·설비 초기화. 다음 생산 준비 완료");
        return true;
    }
    private void BeginHandles()
    {
        makerStartedAt = Now;
        makerAnimation.SetTime(StarchAnimationId.HandleOne, 0d);
        makerAnimation.SetTime(StarchAnimationId.HandleTwo, 0d);
    }
    public bool TryPlaceTrayOnMaker(PlayerInteractionController player)
    {
        if (SieveState != StarchSieveState.ReadyToDry || TrayState != StarchTrayState.CarriedWet)
            return Fail("상단 배치 실패: 두 손잡이와 Drop을 먼저 완료해야 함");
        if (!dryingAnchor || (player && !dryingTray.CarryItem.TryPlaceAt(player, dryingAnchor)))
            return Fail("상단 배치 실패: 건조 위치 또는 손 슬롯 확인 실패");
        SieveState = StarchSieveState.Drying;
        TrayState = StarchTrayState.Drying;
        dryingEndsAt = Now + dryingSeconds;
        nextDryingLogAt = Now + dryingLogIntervalSeconds;
        Log($"재배기 위에 전분 틀 배치. 건조 시작={dryingSeconds:0.#}초");
        return true;
    }
    public bool TryStartPackaging(PlayerInteractionController player)
    {
        if (TrayState != StarchTrayState.ReadyToPack || SieveState != StarchSieveState.ReadyToPack || RemainingPackCount <= 0)
            return Fail("포장 실패: 위 틀의 건조 완료 후 한 팩씩 포장할 수 있음");
        var inventory = player ? player.GetComponent<StarchPackagingInventory>() : null;
        if (!inventory || !inventory.IsHoldingWrapper(player)) return Fail("포장 실패: 포장지를 손에 들어야 함");
        if (!inventory || !inventory.TryBeginPackaging(this)) return Fail("포장 실패: 포장지가 없거나 이미 포장 중");
        packagingInventory = inventory;
        packagingPlayer = player;
        packagingEndsAt = Now + packagingSeconds;
        TrayState = StarchTrayState.Packing;
        Log($"포장 시작. 포장지 1개 소비. {packagingSeconds:0.#}초 뒤 완료. 보유 포장지={inventory.WrapperCount}개");
        return true;
    }
    private bool CanContinuePackaging() => packagingPlayer && packagingPlayer.isActiveAndEnabled &&
        packagingInventory && packagingInventory.PackagingOwner == this &&
        packagingInventory.IsHoldingWrapper(packagingPlayer);

    private void CancelPackaging()
    {
        if (packagingInventory) packagingInventory.CancelPackaging(this);
        packagingInventory = null;
        packagingPlayer = null;
        packagingEndsAt = 0d;
        if (TrayState == StarchTrayState.Packing) TrayState = StarchTrayState.ReadyToPack;
    }

    private void EvaluatePackaging()
    {
        if (TrayState != StarchTrayState.Packing) return;
        if (!CanContinuePackaging()) { CancelPackaging(); return; }
        if (Now + 1e-8 < packagingEndsAt) return;
        packagingInventory.CompletePackaging(this);
        packagingInventory = null;
        packagingPlayer = null;
        packagingEndsAt = 0d;
        LastProducedPackCount = 1;
        TotalProducedPackCount++;
        RemainingPackCount--;
        makerAnimation.SetTime(StarchAnimationId.TrayFill,
            MakerLength(StarchAnimationId.TrayFill) * RemainingPackCount / starchPacksPerCycle,
            dryingTray.GetComponent<ProcessingCarryRig>());
        if (RemainingPackCount == 0)
        {
            TrayState = StarchTrayState.EmptyOnTop;
            Log("모든 전분 포장 완료. 아래 바스켓을 들고 가공기에 [E]로 빈 틀을 내리세요");
        }
        else
        {
            TrayState = StarchTrayState.ReadyToPack;
            Log($"포장 완료. 남은 전분={RemainingPackCount}팩. 다음 [E] 입력 대기");
        }
    }
    [ContextMenu("Debug/Reset Processing Cycle")]
    public void ResetCycle()
    {
        CancelPackaging();
        blender?.CarryItem?.ResetHome();
        dryingTray?.CarryItem?.ResetHome();
        waterBasket?.CarryItem?.ResetHome();
        blenderAnimation?.ResetPose();
        makerAnimation?.ResetPose();
        BlenderState = StarchBlenderState.DockedEmpty;
        SieveState = StarchSieveState.Filling;
        TrayState = StarchTrayState.InstalledEmpty;
        WaterBasketState = StarchWaterBasketState.Docked;
        waterBasket?.RefreshCollider();
        SieveFillCount = 0;
        RemainingPackCount = 0;
        blenderStartedAt = makerStartedAt = dryingEndsAt = nextDryingLogAt = 0d;
    }
    private void EvaluateTimers()
    {
        EnsureInitialized();
        EvaluatePackaging();
        // Automatic stages consume elapsed time in order; explicit input stages never auto-advance.
        for (int guard = 0; guard < 4; guard++)
        {
            StarchAnimationId id;
            switch (BlenderState)
            {
                case StarchBlenderState.LidOpening: id = StarchAnimationId.BlenderOpen; break;
                case StarchBlenderState.LidClosing: id = StarchAnimationId.BlenderClose; break;
                case StarchBlenderState.Starting: id = StarchAnimationId.BlenderStart; break;
                case StarchBlenderState.Working: id = StarchAnimationId.BlenderWorking; break;
                case StarchBlenderState.Ending: id = StarchAnimationId.BlenderEnd; break;
                default: goto Maker;
            }
            double elapsed = Now - blenderStartedAt, length = BlenderLength(id);
            blenderAnimation.SetTime(id, elapsed);
            if (elapsed + 1e-8 < length) break;
            blenderStartedAt += length;
            switch (BlenderState)
            {
                case StarchBlenderState.LidOpening:
                    Log($"감자 {potatoesPerBatch}개 소비(테스트). 뚜껑 닫기");
                    BlenderState = StarchBlenderState.LidClosing; break;
                case StarchBlenderState.LidClosing: BlenderState = StarchBlenderState.LoadedReady; Log("뚜껑 닫기 완료. [E] 가동 대기"); break;
                case StarchBlenderState.Starting: BlenderState = StarchBlenderState.Working; break;
                case StarchBlenderState.Working: BlenderState = StarchBlenderState.Ending; break;
                case StarchBlenderState.Ending: BlenderState = StarchBlenderState.GroundReady; Log("End 완료. 믹서기 통 운반 가능"); break;
            }
        }
        Maker:
        for (int guard = 0; guard < 3; guard++)
        {
            double elapsed = Now - makerStartedAt, length;
            StarchAnimationId id;
            switch (SieveState)
            {
                case StarchSieveState.FillingSegment:
                    length = MakerLength(StarchAnimationId.BasketFill) / Capacity;
                    makerAnimation.SetTime(StarchAnimationId.BasketFill,
                        (SieveFillCount - 1) * length + Math.Min(elapsed, length));
                    if (elapsed + 1e-8 < length) goto Drying;
                    makerStartedAt += length;
                    SieveState = SieveFillCount < Capacity ? StarchSieveState.Filling : StarchSieveState.Draining;
                    Log($"빨간 통 채우기 {SieveFillCount * 20}% 완료");
                    continue;
                case StarchSieveState.Draining:
                    makerAnimation.SetTime(StarchAnimationId.BasketWorking, elapsed);
                    makerAnimation.SetTime(StarchAnimationId.TrayFill, elapsed);
                    if (elapsed + 1e-8 < DrainingDuration) goto Drying;
                    SieveState = StarchSieveState.WetStarchReady;
                    TrayState = StarchTrayState.InstalledWet;
                    Log("전분 배출과 아래 틀 채우기 완료");
                    goto Drying;
                case StarchSieveState.HandlesMoving:
                case StarchSieveState.DryHandlesMoving:
                    makerAnimation.SetTime(StarchAnimationId.HandleOne, elapsed);
                    makerAnimation.SetTime(StarchAnimationId.HandleTwo, elapsed);
                    if (elapsed + 1e-8 < HandlesDuration) goto Drying;
                    makerStartedAt += HandlesDuration;
                    makerAnimation.SetTime(StarchAnimationId.HandleOne, 0d);
                    makerAnimation.SetTime(StarchAnimationId.HandleTwo, 0d);
                    bool dry = SieveState == StarchSieveState.DryHandlesMoving;
                    if (dry)
                    {
                        // Rejoin the source skeleton at the identical upper pose, then let the
                        // original imported drop curves bring the visible tray to the ground.
                        makerAnimation.SetTime(StarchAnimationId.DryBasketDrop, 0d);
                        dryingTray.CarryItem.ResetHome();
                    }
                    else makerAnimation.SetTime(StarchAnimationId.BasketDrop, 0d);
                    SieveState = dry ? StarchSieveState.DryDropping : StarchSieveState.Dropping;
                    Log(dry ? "두 손잡이 완료. Dry Basket Drop 시작" : "두 손잡이 완료. Water Basket Drop 시작");
                    continue;
                case StarchSieveState.Dropping: id = StarchAnimationId.BasketDrop; break;
                case StarchSieveState.DryDropping: id = StarchAnimationId.DryBasketDrop; break;
                default: goto Drying;
            }
            length = MakerLength(id);
            makerAnimation.SetTime(id, elapsed);
            if (elapsed + 1e-8 < length) break;
            makerStartedAt += length;
            if (SieveState == StarchSieveState.DryDropping)
            {
                SieveState = StarchSieveState.WaitingForBasketReturn;
                Log("빈 전분 틀 내리기 완료. 가공기에 다시 [E]로 바스켓 복귀 및 설비 초기화");
            }
            else
            {
                SieveState = StarchSieveState.ReadyToDry;
                WaterBasketState = StarchWaterBasketState.Lowered;
                waterBasket.RefreshCollider();
                Log("Drop 완료. [E] 재배기 위 틀 배치 가능");
            }
        }
        Drying:
        if (TrayState != StarchTrayState.Drying) return;
        while (nextDryingLogAt < dryingEndsAt && Now >= nextDryingLogAt)
        {
            Log($"건조 진행. 남은 시간={dryingEndsAt - nextDryingLogAt:0.#}초");
            nextDryingLogAt += dryingLogIntervalSeconds;
        }
        if (Now + 1e-8 < dryingEndsAt) return;
        TrayState = StarchTrayState.ReadyToPack;
        SieveState = StarchSieveState.ReadyToPack;
        RemainingPackCount = starchPacksPerCycle;
        Log("건조 완료. 포장지를 들고 위 전분 틀에 [E]로 포장하세요");
    }
    private void Log(string message) => Debug.Log(Prefix + message, this);
    private bool Fail(string message) { Debug.LogWarning(Prefix + message, this); return false; }
}
