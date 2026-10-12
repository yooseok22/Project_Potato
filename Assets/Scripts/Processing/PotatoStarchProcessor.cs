using System;
using UnityEngine;

public enum StarchBlenderState : byte
{
    DockedEmpty,
    Grinding,
    GroundReady,
    CarriedFull,
    CarriedEmpty
}

public enum StarchSieveState : byte
{
    Filling,
    Draining,
    WetStarchReady,
    TrayRemoved
}

public enum StarchTrayState : byte
{
    InstalledEmpty,
    InstalledWet,
    CarriedWet,
    Drying,
    Dried
}

[DisallowMultipleComponent]
public sealed class PotatoStarchProcessor : MonoBehaviour
{
    private const string Prefix = "[PotatoStarch] ";

    [Header("References")]
    [SerializeField] private WorldClock clock;
    [SerializeField] private StarchBlender blender;
    [SerializeField] private StarchSieve sieve;
    [SerializeField] private StarchDryingTray dryingTray;

    [Header("Test Timing")]
    [SerializeField, Min(0.1f)] private float grindingSeconds = 5f;
    [SerializeField, Min(0.1f)] private float drainingSeconds = 5f;
    [SerializeField, Min(0.1f)] private float dryingSeconds = 150f;
    [SerializeField, Min(0.1f)] private float dryingLogIntervalSeconds = 30f;

    [Header("Capacity")]
    [SerializeField, Min(1)] private int potatoesPerBatch = 4;
    [SerializeField, Min(1)] private int sieveBatchCapacity = 5;
    [SerializeField, Min(1)] private int starchPacksPerCycle = 5;

    private WorldClock boundClock;
    private double fallbackTime;
    private double grindingEndsAt;
    private double drainingEndsAt;
    private double dryingStartedAt;
    private double dryingEndsAt;
    private double nextDryingLogAt;

    public StarchBlenderState BlenderState { get; private set; }
    public StarchSieveState SieveState { get; private set; }
    public StarchTrayState TrayState { get; private set; }
    public int SieveFillCount { get; private set; }
    public int SieveBatchCapacity => sieveBatchCapacity;
    public int LastProducedPackCount { get; private set; }
    public int TotalProducedPackCount { get; private set; }
    public int GrindingSecondsRemaining => RemainingSeconds(grindingEndsAt);
    public int DrainingSecondsRemaining => RemainingSeconds(drainingEndsAt);
    public int DryingSecondsRemaining => RemainingSeconds(dryingEndsAt);

    private double Now => clock ? clock.ElapsedSeconds : fallbackTime;

    private void Awake()
    {
        ResolveReferences();
        BindClock();
    }

    private void OnEnable() => BindClock();

    private void Start()
    {
        ResolveReferences();
        BindClock();
        if (!clock) Debug.LogWarning(Prefix + "WorldClock을 찾지 못해 Unity 재생 시간을 사용합니다.", this);
        Log($"테스트 가공 설비 준비 완료. 분쇄={grindingSeconds:0.#}초 × {sieveBatchCapacity}회, " +
            $"물 빼기={drainingSeconds:0.#}초, 건조={dryingSeconds:0.#}초, 결과={starchPacksPerCycle}팩");
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
        StarchDryingTray tray)
    {
        if (boundClock) boundClock.TimeAdvanced -= EvaluateTimers;
        clock = worldClock;
        blender = starchBlender;
        sieve = starchSieve;
        dryingTray = tray;
        ResolveReferences();
        BindClock();
    }

    public void ConfigureTestValues(float grindSeconds, float drainSeconds, float drySeconds,
        float dryLogInterval = 30f)
    {
        grindingSeconds = Mathf.Max(0.1f, grindSeconds);
        drainingSeconds = Mathf.Max(0.1f, drainSeconds);
        dryingSeconds = Mathf.Max(0.1f, drySeconds);
        dryingLogIntervalSeconds = Mathf.Max(0.1f, dryLogInterval);
    }

    public bool TryStartGrinding()
    {
        if (BlenderState != StarchBlenderState.DockedEmpty)
            return Fail("믹서기 가동 실패: 현재 상태=" + BlenderState);
        if (SieveState != StarchSieveState.Filling)
            return Fail("믹서기 가동 실패: 현재 생산 사이클을 먼저 완료해야 함");

        BlenderState = StarchBlenderState.Grinding;
        grindingEndsAt = Now + grindingSeconds;
        Log($"감자 {potatoesPerBatch}개 소비(테스트). 믹서기 가동 시작. 남은 시간={grindingSeconds:0.#}초");
        return true;
    }

    public bool TryTakeBlender(PlayerInteractionController player)
    {
        if (BlenderState == StarchBlenderState.Grinding)
            return Fail($"믹서기 들기 실패: 아직 분쇄 중. 남은 시간={RemainingPrecise(grindingEndsAt):0.0}초");
        if (BlenderState != StarchBlenderState.GroundReady)
            return Fail("믹서기 들기 실패: 갈아진 감자가 준비되지 않음");
        if (player && (!blender || !blender.CarryItem || !blender.CarryItem.TryAttach(player)))
            return Fail("믹서기 들기 실패: 플레이어 손 슬롯을 사용할 수 없음");

        BlenderState = StarchBlenderState.CarriedFull;
        Log($"믹서기 들기 완료. 상태=갈린 감자 {potatoesPerBatch}개");
        return true;
    }

    public bool TryPourBatch(PlayerInteractionController player)
    {
        if (BlenderState != StarchBlenderState.CarriedFull)
            return Fail("체 통 투입 실패: 들고 있는 믹서기가 비어 있거나 없음");
        if (player && (!blender || !blender.CarryItem || !blender.CarryItem.IsCarriedBy(player)))
            return Fail("체 통 투입 실패: 해당 플레이어가 믹서기를 들고 있지 않음");
        if (SieveState != StarchSieveState.Filling || SieveFillCount >= sieveBatchCapacity)
            return Fail("체 통 투입 실패: 체 통이 가득 찼거나 물 빼기 중");

        SieveFillCount++;
        BlenderState = StarchBlenderState.CarriedEmpty;
        Log($"체 통에 갈린 감자 투입 완료. 용량={SieveFillCount}/{sieveBatchCapacity}");

        if (SieveFillCount == sieveBatchCapacity)
        {
            SieveState = StarchSieveState.Draining;
            drainingEndsAt = Now + drainingSeconds;
            Log($"체 통 용량={SieveFillCount}/{sieveBatchCapacity}. 물 빼기 시작. 남은 시간={drainingSeconds:0.#}초");
        }
        return true;
    }

    public bool TryReturnBlender(PlayerInteractionController player)
    {
        if (BlenderState == StarchBlenderState.CarriedFull)
            return Fail("믹서기 원위치 실패: 갈린 감자를 먼저 체 통에 부어야 함");
        if (BlenderState != StarchBlenderState.CarriedEmpty)
            return Fail("믹서기 원위치 실패: 빈 믹서기를 들고 있지 않음");
        if (player && (!blender || !blender.CarryItem || !blender.CarryItem.TryReturnHome(player)))
            return Fail("믹서기 원위치 실패: 손 슬롯의 믹서기를 확인할 수 없음");

        BlenderState = StarchBlenderState.DockedEmpty;
        Log("빈 믹서기 원위치 완료");
        return true;
    }

    public bool TryTakeWetTray(PlayerInteractionController player)
    {
        if (SieveState != StarchSieveState.WetStarchReady || TrayState != StarchTrayState.InstalledWet)
            return Fail("건조 틀 꺼내기 실패: 물 빼기가 완료되지 않음");
        if (player && (!dryingTray || !dryingTray.CarryItem || !dryingTray.CarryItem.TryAttach(player)))
            return Fail("건조 틀 꺼내기 실패: 플레이어 손 슬롯을 사용할 수 없음");

        SieveState = StarchSieveState.TrayRemoved;
        TrayState = StarchTrayState.CarriedWet;
        Log("젖은 전분 건조 틀 꺼내기 완료");
        return true;
    }

    public bool TryPlaceTray(PlayerInteractionController player, Vector3 position, Quaternion rotation,
        bool isOutdoorSurface)
    {
        if (TrayState != StarchTrayState.CarriedWet)
            return Fail("건조 틀 배치 실패: 젖은 전분 건조 틀을 들고 있지 않음");
        if (!isOutdoorSurface)
            return Fail("건조 틀 배치 실패: 실외 바닥이 아님");
        if (player && (!dryingTray || !dryingTray.CarryItem || !dryingTray.CarryItem.IsCarriedBy(player)))
            return Fail("건조 틀 배치 실패: 해당 플레이어가 건조 틀을 들고 있지 않음");
        if (player && !dryingTray.CarryItem.TryPlace(player, position, rotation))
            return Fail("건조 틀 배치 실패: 손 슬롯에서 틀을 분리할 수 없음");

        TrayState = StarchTrayState.Drying;
        dryingStartedAt = Now;
        dryingEndsAt = Now + dryingSeconds;
        nextDryingLogAt = Now + dryingLogIntervalSeconds;
        Log($"건조 틀을 실외 바닥에 배치. 건조 시작. 남은 시간={dryingSeconds:0.#}초");
        return true;
    }

    public bool TryCollectProduct()
    {
        if (TrayState == StarchTrayState.Drying)
            return Fail($"전분 수거 실패: 아직 건조 중. 남은 시간={RemainingPrecise(dryingEndsAt):0.0}초");
        if (TrayState != StarchTrayState.Dried)
            return Fail("전분 수거 실패: 건조가 완료된 전분이 없음");

        LastProducedPackCount = starchPacksPerCycle;
        TotalProducedPackCount += starchPacksPerCycle;
        Log($"전분 팩 {starchPacksPerCycle}개 생성(테스트). 실제 인벤토리 지급 없음");
        ResetCycle();
        Log("가공 설비가 다음 테스트 사이클을 위해 초기화됨");
        return true;
    }

    [ContextMenu("Debug/Reset Processing Cycle")]
    public void ResetCycle()
    {
        BlenderState = StarchBlenderState.DockedEmpty;
        SieveState = StarchSieveState.Filling;
        TrayState = StarchTrayState.InstalledEmpty;
        SieveFillCount = 0;
        grindingEndsAt = drainingEndsAt = dryingStartedAt = dryingEndsAt = nextDryingLogAt = 0d;
        blender?.CarryItem?.ResetHome();
        dryingTray?.CarryItem?.ResetHome();
    }

    private void ResolveReferences()
    {
        if (!clock) clock = FindAnyObjectByType<WorldClock>();
        if (!blender) blender = GetComponentInChildren<StarchBlender>(true);
        if (!sieve) sieve = GetComponentInChildren<StarchSieve>(true);
        if (!dryingTray) dryingTray = GetComponentInChildren<StarchDryingTray>(true);
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

    private void EvaluateTimers()
    {
        double now = Now;
        if (BlenderState == StarchBlenderState.Grinding && now >= grindingEndsAt)
        {
            BlenderState = StarchBlenderState.GroundReady;
            Log("믹서기 가동 끝. 감자가 갈아진 상태");
        }

        if (SieveState == StarchSieveState.Draining && now >= drainingEndsAt)
        {
            SieveState = StarchSieveState.WetStarchReady;
            TrayState = StarchTrayState.InstalledWet;
            Log("물 빼기 완료. 젖은 전분이 건조 틀에 준비됨");
        }

        if (TrayState != StarchTrayState.Drying) return;
        while (nextDryingLogAt < dryingEndsAt && now >= nextDryingLogAt)
        {
            double elapsed = nextDryingLogAt - dryingStartedAt;
            double remaining = dryingEndsAt - nextDryingLogAt;
            Log($"건조 진행. 경과={elapsed:0.#}/{dryingSeconds:0.#}초, 남은 시간={remaining:0.#}초");
            nextDryingLogAt += dryingLogIntervalSeconds;
        }
        if (now < dryingEndsAt) return;
        TrayState = StarchTrayState.Dried;
        Log($"건조 완료. 전분 팩 {starchPacksPerCycle}개 준비됨");
    }

    private int RemainingSeconds(double endsAt) => (int)Math.Ceiling(RemainingPrecise(endsAt));
    private double RemainingPrecise(double endsAt) => Math.Max(0d, endsAt - Now);
    private void Log(string message) => Debug.Log(Prefix + message, this);
    private bool Fail(string message)
    {
        Debug.LogWarning(Prefix + message, this);
        return false;
    }
}
