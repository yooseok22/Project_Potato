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
    HandleOneMoving, HandleOneDone, HandleTwoMoving, Dropping, ReadyToDry, Drying
}
public enum StarchTrayState : byte { InstalledEmpty, InstalledWet, CarriedWet, Drying, Dried }

[DisallowMultipleComponent]
public sealed class PotatoStarchProcessor : MonoBehaviour
{
    private const string Prefix = "[PotatoStarch] ";
    [SerializeField] private WorldClock clock;
    [SerializeField] private StarchBlender blender;
    [SerializeField] private StarchSieve sieve;
    [SerializeField] private StarchDryingTray dryingTray;
    [SerializeField] private StarchPropAnimationDriver blenderAnimation;
    [SerializeField] private StarchPropAnimationDriver makerAnimation;
    [SerializeField] private Transform dryingAnchor;
    [SerializeField, Min(0.1f)] private float dryingSeconds = 150f;
    [SerializeField, Min(0.1f)] private float dryingLogIntervalSeconds = 30f;
    [SerializeField, Min(1)] private int potatoesPerBatch = 4;
    [SerializeField, Min(1)] private int starchPacksPerCycle = 5;
    private const int Capacity = 5;
    private WorldClock boundClock;
    private double fallbackTime, blenderStartedAt, makerStartedAt, dryingEndsAt, nextDryingLogAt;
    private bool initialized;

    public StarchBlenderState BlenderState { get; private set; }
    public StarchSieveState SieveState { get; private set; }
    public StarchTrayState TrayState { get; private set; }
    public int SieveFillCount { get; private set; }
    public int SieveBatchCapacity => Capacity;
    public int LastProducedPackCount { get; private set; }
    public int TotalProducedPackCount { get; private set; }
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
        if (clock) return;
        fallbackTime += Time.deltaTime;
        EvaluateTimers();
    }
    private void OnDisable()
    {
        if (boundClock) boundClock.TimeAdvanced -= EvaluateTimers;
        boundClock = null;
    }
    public void Configure(WorldClock worldClock, StarchBlender starchBlender, StarchSieve starchSieve,
        StarchDryingTray tray, StarchPropAnimationDriver blenderDriver,
        StarchPropAnimationDriver makerDriver, Transform topAnchor)
    {
        if (boundClock) boundClock.TimeAdvanced -= EvaluateTimers;
        boundClock = null;
        clock = worldClock;
        blender = starchBlender;
        sieve = starchSieve;
        dryingTray = tray;
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
        if (!blenderAnimation || !makerAnimation || !dryingAnchor)
            throw new InvalidOperationException("Starch props need animation drivers and a top drying anchor.");
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
        if (TrayState != StarchTrayState.CarriedWet || (player && !dryingTray.CarryItem.IsCarriedBy(player)))
            return Fail("재배기 조작 실패: 전분 틀을 들어야 함");
        switch (SieveState)
        {
            case StarchSieveState.TrayRemoved:
                SieveState = StarchSieveState.HandleOneMoving;
                makerStartedAt = Now;
                makerAnimation.SetTime(StarchAnimationId.HandleOne, 0d);
                Log("첫 번째 손잡이 당기기");
                return true;
            case StarchSieveState.HandleOneDone:
                SieveState = StarchSieveState.HandleTwoMoving;
                makerStartedAt = Now;
                makerAnimation.SetTime(StarchAnimationId.HandleTwo, 0d);
                Log("두 번째 손잡이 당기기 (H2-1 Use)");
                return true;
            case StarchSieveState.ReadyToDry:
                return TryPlaceTrayOnMaker(player);
            default:
                return Fail("재배기 조작 실패: 현재 애니메이션이 끝나야 함");
        }
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
    public bool TryCollectProduct()
    {
        if (TrayState != StarchTrayState.Dried) return Fail("전분 수거 실패: 건조가 완료되지 않음");
        LastProducedPackCount = starchPacksPerCycle;
        TotalProducedPackCount += starchPacksPerCycle;
        ResetCycle();
        Log($"전분 {starchPacksPerCycle}팩 획득(테스트). 실제 인벤토리 지급 없음");
        Log("빈 전분 틀 아래 슬롯 복귀. 다음 생산 준비 완료");
        return true;
    }
    [ContextMenu("Debug/Reset Processing Cycle")]
    public void ResetCycle()
    {
        blender?.CarryItem?.ResetHome();
        dryingTray?.CarryItem?.ResetHome();
        blenderAnimation?.ResetPose();
        makerAnimation?.ResetPose();
        BlenderState = StarchBlenderState.DockedEmpty;
        SieveState = StarchSieveState.Filling;
        TrayState = StarchTrayState.InstalledEmpty;
        SieveFillCount = 0;
        blenderStartedAt = makerStartedAt = dryingEndsAt = nextDryingLogAt = 0d;
    }
    private void EvaluateTimers()
    {
        EnsureInitialized();
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
                case StarchSieveState.HandleOneMoving: id = StarchAnimationId.HandleOne; break;
                case StarchSieveState.HandleTwoMoving: id = StarchAnimationId.HandleTwo; break;
                case StarchSieveState.Dropping: id = StarchAnimationId.BasketDrop; break;
                default: goto Drying;
            }
            length = MakerLength(id);
            makerAnimation.SetTime(id, elapsed);
            if (elapsed + 1e-8 < length) break;
            makerStartedAt += length;
            if (SieveState == StarchSieveState.HandleOneMoving) SieveState = StarchSieveState.HandleOneDone;
            else if (SieveState == StarchSieveState.HandleTwoMoving)
            {
                makerAnimation.SetTime(StarchAnimationId.HandleOne, 0d);
                makerAnimation.SetTime(StarchAnimationId.HandleTwo, 0d);
                makerAnimation.SetTime(StarchAnimationId.BasketDrop, 0d);
                SieveState = StarchSieveState.Dropping;
                Log("두 손잡이 완료. 손잡이 원위치 및 빨간 통 Drop 시작");
            }
            else { SieveState = StarchSieveState.ReadyToDry; Log("Drop 완료. [E] 재배기 위 틀 배치 가능"); }
        }
        Drying:
        if (TrayState != StarchTrayState.Drying) return;
        while (nextDryingLogAt < dryingEndsAt && Now >= nextDryingLogAt)
        {
            Log($"건조 진행. 남은 시간={dryingEndsAt - nextDryingLogAt:0.#}초");
            nextDryingLogAt += dryingLogIntervalSeconds;
        }
        if (Now + 1e-8 < dryingEndsAt) return;
        TrayState = StarchTrayState.Dried;
        Log("건조 완료. 전분 틀에서 [E]로 수거 가능");
    }
    private void Log(string message) => Debug.Log(Prefix + message, this);
    private bool Fail(string message) { Debug.LogWarning(Prefix + message, this); return false; }
}
