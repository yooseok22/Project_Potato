using System;
using UnityEngine;

/// <summary>A tile-independent plant using its planting timestamp on a shared clock.</summary>
[DisallowMultipleComponent]
public sealed class PotatoGrowth : MonoBehaviour
{
    private static readonly double[] StageStarts = { 0d, 40d, 80d, 150d, 220d, 260d, 300d };
    private static readonly string[] StageNames =
        { "흙 언덕", "새싹", "큰 잎 2개", "큰 잎 4개", "큰 잎 6개", "싱싱한 큰 잎 6개", "시든 잎 6개" };

    [SerializeField] private WorldClock clock;
    [Tooltip("For standalone samples. A tile planting system can disable this and call Plant(clock).")]
    [SerializeField] private bool plantOnStart = true;
    [SerializeField] private bool logStageChanges = true;
    [Tooltip("Log progress every N game seconds. Set to 0 to log stage changes only.")]
    [SerializeField, Min(0f)] private float progressLogInterval = 5f;
    private double nextProgressLogAge;
    private double plantedAt;
    private WorldClock subscribedClock;

    public const double BaseGrowthSeconds = PotatoGrowthRules.BaseGrowthSeconds;
    public bool IsPlanted { get; private set; }
    public PotatoGrowthStage Stage { get; private set; }
    public string StageName => StageNames[(int)Stage];
    public double AgeSeconds => IsPlanted && clock ? Math.Max(0d, clock.ElapsedSeconds - plantedAt) : 0d;
    public float Progress => (float)Math.Min(1d, AgeSeconds / BaseGrowthSeconds);
    public bool IsReadyToHarvest => IsPlanted && AgeSeconds >= BaseGrowthSeconds;
    public bool IsWatered { get; private set; }
    public bool StemRemoved { get; private set; }
    public event Action<PotatoGrowthStage> StageChanged;
    public event Action HarvestReady;

    private void Start()
    {
        if (!plantOnStart || IsPlanted) return;
        if (!clock)
        {
            var clocks = FindObjectsByType<WorldClock>(FindObjectsSortMode.None);
            if (clocks.Length == 1) clock = clocks[0];
        }
        if (!clock)
        {
            Debug.LogError("[Potato] Assign one WorldClock or call Plant(clock). Automatic planting requires exactly one active clock.", this);
            return;
        }
        Plant(clock);
    }

    private void OnEnable()
    {
        Subscribe();
        RefreshGrowth();
    }

    private void OnDisable() => Unsubscribe();

    /// <summary>Plants once. Harvest first before reusing a plant; duplicate calls cannot reset growth.</summary>
    public bool Plant(WorldClock worldClock)
    {
        if (!worldClock) throw new ArgumentNullException(nameof(worldClock));
        if (IsPlanted) return false;
        Unsubscribe();
        clock = worldClock;
        plantedAt = clock.ElapsedSeconds;
        Stage = PotatoGrowthStage.SoilMound;
        IsPlanted = true;
        IsWatered = false;
        StemRemoved = false;
        nextProgressLogAge = progressLogInterval;
        Subscribe();
        ReportStage();
        return true;
    }

    public void RefreshGrowth()
    {
        if (!IsPlanted || !clock) return;
        int target = (int)StageAt(AgeSeconds);
        while ((int)Stage < target)
        {
            Stage = (PotatoGrowthStage)((int)Stage + 1);
            ReportStage();
            if (Stage == PotatoGrowthStage.WitheredSixLeaves) HarvestReady?.Invoke();
        }
        if (progressLogInterval > 0f && !IsReadyToHarvest && AgeSeconds >= nextProgressLogAge)
        {
            double remaining = StageStarts[(int)Stage + 1] - AgeSeconds;
            Debug.Log($"[Potato] {name}: {StageName} | 성장 {AgeSeconds:0.0}/300초 | 다음 단계까지 {remaining:0.0}초 | [World] {clock.DisplayTime}", this);
            nextProgressLogAge = AgeSeconds + progressLogInterval;
        }
    }

    public static PotatoGrowthStage StageAt(double ageSeconds) => PotatoGrowthRules.StageAt(ageSeconds);

    public bool TryHarvest()
    {
        if (!IsReadyToHarvest) return false;
        RefreshGrowth();
        IsPlanted = false;
        IsWatered = false;
        StemRemoved = false;
        Unsubscribe();
        if (logStageChanges) Debug.Log($"[Potato] {name}: 수확 완료", this);
        return true;
    }

    public bool TryWater()
    {
        if (!IsPlanted || IsWatered) return false;
        IsWatered = true;
        Debug.Log($"[Potato] {name}: 물 공급 완료", this);
        return true;
    }

    public bool TryCutStem()
    {
        RefreshGrowth();
        if (!IsReadyToHarvest || StemRemoved) return false;
        StemRemoved = true;
        Debug.Log($"[Potato] {name}: 줄기 제거 완료", this);
        return true;
    }

    public bool TryHarvestWithFork() => StemRemoved && TryHarvest();

    private void Subscribe()
    {
        if (!isActiveAndEnabled || !IsPlanted || !clock || subscribedClock == clock) return;
        Unsubscribe();
        subscribedClock = clock;
        subscribedClock.TimeAdvanced += RefreshGrowth;
    }

    private void Unsubscribe()
    {
        if (subscribedClock) subscribedClock.TimeAdvanced -= RefreshGrowth;
        subscribedClock = null;
    }

    private void ReportStage()
    {
        if (logStageChanges)
            Debug.Log($"[Potato] {name}: {StageName} (성장 {AgeSeconds:0.0}/{BaseGrowthSeconds:0}초)" +
                (Stage == PotatoGrowthStage.WitheredSixLeaves ? " / 수확 가능" : ""), this);
        StageChanged?.Invoke(Stage);
    }

    [ContextMenu("Debug/Harvest")]
    private void DebugHarvest()
    {
        if (Application.isPlaying && !TryHarvest()) Debug.Log("[Potato] 아직 수확할 수 없습니다.", this);
    }

    [ContextMenu("Debug/Plant Again")]
    private void DebugPlant()
    {
        if (Application.isPlaying && clock) Plant(clock);
    }
}

