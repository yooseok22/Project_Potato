using System;
using System.Collections.Generic;
using UnityEngine;

public enum PotatoHarvestTiming : byte
{
    Early,
    Optimal,
    Late
}

public enum PotatoHarvestGrade : byte
{
    None = 0, // Abnormal potatoes have conditions instead of a star grade.
    OneStar = 1,
    TwoStar = 2,
    ThreeStar = 3
}

public readonly struct PotatoHarvestResult
{
    public GroundGrid Ground { get; }
    public Vector2Int Cell { get; }
    public PotatoHarvestTiming Timing { get; }
    public PotatoHarvestQuality Quality { get; }
    public string StateCode { get; }
    public float Health { get; }
    public float MaxHealth { get; }
    public PotatoStatus Status { get; }
    public bool HasAbnormalStatus => AbnormalCount > 0;
    /// <summary>None for an abnormal potato; otherwise its one-, two- or three-star grade.</summary>
    public PotatoHarvestGrade[] Grades { get; }
    public HarvestedPotatoCondition[] ItemConditions { get; }
    public int AbnormalCount
    {
        get
        {
            int count = 0;
            if (ItemConditions != null)
                foreach (var condition in ItemConditions) if (condition != HarvestedPotatoCondition.None) count++;
            return count;
        }
    }
    public int Count => Grades?.Length ?? 0;

    public PotatoHarvestResult(GroundGrid ground, Vector2Int cell, PotatoHarvestTiming timing,
        PotatoHarvestQuality quality, string stateCode, float health, float maxHealth,
        PotatoHarvestGrade[] grades, PotatoStatus status = PotatoStatus.None,
        HarvestedPotatoCondition[] itemConditions = null)
    {
        Ground = ground;
        Cell = cell;
        Timing = timing;
        Quality = quality;
        StateCode = stateCode;
        Health = health;
        MaxHealth = maxHealth;
        Status = status;
        grades ??= Array.Empty<PotatoHarvestGrade>();
        if (itemConditions != null && itemConditions.Length != grades.Length)
            throw new ArgumentException("Each harvested potato needs exactly one condition entry.", nameof(itemConditions));
        Grades = (PotatoHarvestGrade[])grades.Clone();
        ItemConditions = itemConditions == null ? new HarvestedPotatoCondition[Grades.Length]
            : (HarvestedPotatoCondition[])itemConditions.Clone();
        // A potato belongs to exactly one reward category. Keep all its condition flags,
        // but never retain a hidden star grade on an abnormal reward.
        for (int index = 0; index < Grades.Length; index++)
            if (ItemConditions[index] != HarvestedPotatoCondition.None) Grades[index] = PotatoHarvestGrade.None;
    }
}

public static class PotatoHarvestRewardRules
{
    public static void GetCountRange(float health, out int minimum, out int maximum)
    {
        health = Mathf.Clamp(health, 0f, 100f);
        if (health >= 100f)
        {
            minimum = 2;
            maximum = 3;
        }
        else if (health >= 90f)
        {
            minimum = 1;
            maximum = 3;
        }
        else if (health >= 80f)
        {
            minimum = 1;
            maximum = 2;
        }
        else if (health > 0f)
        {
            minimum = maximum = 1;
        }
        else
        {
            minimum = maximum = 0;
        }
    }

    public static PotatoHarvestGrade GradeAt(float health, float roll01)
    {
        health = Mathf.Clamp(health, 0f, 100f);
        roll01 = Mathf.Clamp01(roll01);
        if (health >= 100f)
        {
            if (roll01 < 0.70f) return PotatoHarvestGrade.OneStar;
            return roll01 < 0.90f ? PotatoHarvestGrade.TwoStar : PotatoHarvestGrade.ThreeStar;
        }
        if (health >= 90f)
            return roll01 < 0.85f ? PotatoHarvestGrade.OneStar : PotatoHarvestGrade.TwoStar;
        return PotatoHarvestGrade.OneStar;
    }

    /// <summary>Every integer quantity in the HP tier has equal probability.</summary>
    public static int RollCount(float health)
    {
        GetCountRange(health, out int minimum, out int maximum);
        return minimum == maximum ? minimum : UnityEngine.Random.Range(minimum, maximum + 1);
    }

    public static PotatoHarvestGrade[] Roll(float health)
    {
        int count = RollCount(health);
        var grades = new PotatoHarvestGrade[count];
        // Each potato gets its own grade roll; the quantity roll never chooses a batch-wide grade.
        for (int index = 0; index < count; index++)
            grades[index] = GradeAt(health, UnityEngine.Random.value);
        return grades;
    }
}

[DisallowMultipleComponent]
public sealed partial class PotatoHarvestCoordinator : MonoBehaviour
{
    private const float EarlyHarvestDamage = 20f;
    private const float LateHarvestDamage = 40f;

    private readonly Dictionary<(GroundGrid, Vector2Int), PotatoHarvestTiming> harvestTimings = new();
    private readonly HashSet<(GroundGrid, Vector2Int)> coveredCells = new();
    private readonly Dictionary<(GroundGrid, Vector2Int), HarvestedPotatoCondition> harvestConditions = new();
    private PotatoFarmingSystem farmingSystem;

    public event Action<GroundGrid, Vector2Int> LeavesCut;
    public event Action<GroundGrid, Vector2Int> CropDiscarded;

    private void Awake() => Configure(GetComponent<PotatoFarmingSystem>());

    public void Configure(PotatoFarmingSystem system)
    {
        if (farmingSystem == system) return;

        Unsubscribe();
        farmingSystem = system;
        if (farmingSystem)
        {
            farmingSystem.FarmingStageChanged += OnFarmingStageChanged;
            farmingSystem.FarmingTileDied += OnCropDied;
        }
    }

    public void ResetCell(GroundGrid ground, Vector2Int cell)
    {
        harvestTimings.Remove((ground, cell));
        coveredCells.Remove((ground, cell));
        harvestConditions.Remove((ground, cell));
    }

    /// <summary>Connect condition sources such as sprouting without changing the growth/health owner's enum.</summary>
    public bool TryAddHarvestCondition(GroundGrid ground, Vector2Int cell, HarvestedPotatoCondition condition)
    {
        if (condition == HarvestedPotatoCondition.None || (condition & ~PotatoHarvestConditionRules.Harvestable) != 0 ||
            !farmingSystem || !farmingSystem.TryGetFarmingTile(ground, cell, out var crop) || crop.IsDead) return false;
        harvestConditions.TryGetValue((ground, cell), out var previous);
        if ((previous | condition) == previous) return false;
        harvestConditions[(ground, cell)] = previous | condition;
        return true;
    }

    public void MarkCovered(GroundGrid ground, Vector2Int cell)
    {
        if (ground && ground.ContainsCell(cell)) coveredCells.Add((ground, cell));
        if (farmingSystem && farmingSystem.TryGetFarmingTile(ground, cell, out var tile))
            tile.IsSoilCovered = true;
    }

    public bool CanCutForHarvest(GroundGrid ground, Vector2Int cell)
    {
        if (!farmingSystem || !ground || !ground.CanUseSickle(cell)
            || harvestTimings.ContainsKey((ground, cell))) return false;
        if (!farmingSystem.TryGetFarmingTile(ground, cell, out var tile)) return false;
        return !tile.IsDead && SickleHarvestPreviewController.IsHarvestStage(tile.Stage) &&
            (tile.Stage == PotatoGrowthStage.WitheredSixLeaves || tile.IsWatered && !tile.IsGrowthPaused);
    }

    public bool TryCutForHarvest(GroundGrid ground, Vector2Int cell)
    {
        if (!CanCutForHarvest(ground, cell)) return false;

        GroundCellState state = ground.GetCellState(cell);
        if (!TryGetHarvestTiming(state, out PotatoHarvestTiming timing, out bool covered)) return false;
        if (!farmingSystem.TryGetFarmingTile(ground, cell, out var tile)) return false;
        float healthBeforeCut = tile.CurrentHealth;
        bool pausedByThisInteraction = tile.Stage != PotatoGrowthStage.WitheredSixLeaves;
        if (pausedByThisInteraction && !farmingSystem.TryPauseGrowth(ground, cell)) return false;
        if (!ground.TryUseSickle(cell))
        {
            if (pausedByThisInteraction) farmingSystem.TryResumeGrowth(ground, cell);
            Debug.LogError($"[Potato Harvest] Growth stopped but the sickle tile transition failed. Cell={cell}", this);
            return false;
        }

        harvestTimings.Add((ground, cell), timing);
        if (covered) coveredCells.Add((ground, cell));

        float damage = timing == PotatoHarvestTiming.Early ? EarlyHarvestDamage
            : timing == PotatoHarvestTiming.Late ? LateHarvestDamage : 0f;
        if (damage > 0f && !farmingSystem.TryDamage(ground, cell, damage))
            Debug.LogWarning($"[Potato Harvest] Failed to apply {damage:0.#} harvest damage. Cell={cell}", this);
        Debug.Log($"[Potato Harvest] 잎 베기 완료. Cell={cell}, " +
            $"수확시기={SickleHarvestPreviewController.HarvestStateName(tile.Stage)}, 시기 피해={damage:0.#}, " +
            $"HP={healthBeforeCut:0.#} → {tile.CurrentHealth:0.#}, 재배 상태={PotatoHarvestConditionRules.Describe(PotatoHarvestConditionRules.FromCrop(tile.Status))}", this);
        LeavesCut?.Invoke(ground, cell);
        return true;
    }

    public bool TryUsePotatoFork(GroundGrid ground, Vector2Int cell)
    {
        if (!ground || !ground.CanUsePotatoFork(cell) || !farmingSystem ||
            !harvestTimings.ContainsKey((ground, cell)) ||
            !farmingSystem.TryGetFarmingTile(ground, cell, out var tile) || tile.IsDead) return false;
        GroundCellState state = ground.GetCellState(cell);
        bool discardsCrop = IsForkDiscardState(state);
        if (discardsCrop && (!farmingSystem || !farmingSystem.StopFarming(ground, cell))) return false;
        if (!ground.TryUsePotatoFork(cell)) return false;
        if (discardsCrop)
        {
            ResetCell(ground, cell);
            CropDiscarded?.Invoke(ground, cell);
        }
        else if (ground.TryGetHarvestInfo(cell, out var quality, out _) && quality == PotatoHarvestQuality.Damaged)
            farmingSystem.TryAddStatus(ground, cell, PotatoStatus.Injured);
        return true;
    }

    public bool CanHarvest(GroundGrid ground, Vector2Int cell)
    {
        return farmingSystem && ground && harvestTimings.ContainsKey((ground, cell))
            && ground.CanHarvestPotato(cell)
            && farmingSystem.TryGetFarmingTile(ground, cell, out var tile) && !tile.IsDead;
    }

    public bool TryHarvest(GroundGrid ground, Vector2Int cell, out PotatoHarvestResult result)
    {
        result = default;
        var key = (ground, cell);
        if (!CanHarvest(ground, cell)
            || !harvestTimings.TryGetValue(key, out PotatoHarvestTiming timing)
            || !ground.TryGetHarvestInfo(cell, out PotatoHarvestQuality quality, out string stateCode)
            || !farmingSystem.TryGetFarmingTile(ground, cell, out var tile)) return false;

        float maximumHealth = tile.MaxHealth;
        if (!farmingSystem.TryCompleteHarvest(ground, cell, out float health)) return false;
        if (!ground.TrySetCellState(cell, GroundCellState.HarvestClearedField))
        {
            Debug.LogError($"[Potato Harvest] Farming data completed but N_015 transition failed. Cell={cell}", this);
            return false;
        }

        var grades = PotatoHarvestRewardRules.Roll(health);
        PotatoStatus status = tile.Status | (quality == PotatoHarvestQuality.Damaged ? PotatoStatus.Injured : PotatoStatus.None);
        harvestConditions.TryGetValue(key, out var additionalConditions);
        var conditions = PotatoHarvestConditionRules.Roll(health, grades.Length,
            PotatoHarvestConditionRules.FromCrop(status) | additionalConditions);
        result = new PotatoHarvestResult(ground, cell, timing, quality, stateCode,
            health, maximumHealth, grades, status, conditions);
        ResetCell(ground, cell);
        LogResult(result);
        return true;
    }

    public static bool TryGetHarvestTiming(GroundCellState state,
        out PotatoHarvestTiming timing, out bool covered)
    {
        covered = state >= GroundCellState.SoilCoveredThird && state <= GroundCellState.SoilCoveredFifth;
        switch (state)
        {
            case GroundCellState.EarlyHarvest:
            case GroundCellState.SoilCoveredThird:
                timing = PotatoHarvestTiming.Early;
                return true;
            case GroundCellState.ReadyToHarvest:
            case GroundCellState.SoilCoveredFourth:
                timing = PotatoHarvestTiming.Optimal;
                return true;
            case GroundCellState.LateHarvest:
            case GroundCellState.SoilCoveredFifth:
                timing = PotatoHarvestTiming.Late;
                return true;
            default:
                timing = default;
                covered = false;
                return false;
        }
    }

    private void OnFarmingStageChanged(GroundGrid targetGround, Vector2Int cell, PotatoGrowthStage stage)
    {
        // A deadline can kill a crop inside ForwardStage; its remaining visual update must not revive it.
        if (targetGround && farmingSystem &&
            (!farmingSystem.TryGetFarmingTile(targetGround, cell, out var crop) || crop.IsDead))
        {
            targetGround.TrySetCellState(cell, GroundCellState.BoarDugField);
            return;
        }
        if (!targetGround || !coveredCells.Contains((targetGround, cell))
            || !TryGetCoveredGrowthState(stage, out GroundCellState state)) return;
        targetGround.TrySetCellState(cell, state);
    }

    private void OnCropDied(GroundGrid ground, Vector2Int cell)
    {
        if (!ground) return;
        ground.TrySetCellState(cell, GroundCellState.BoarDugField);
        farmingSystem.StopFarming(ground, cell);
        ResetCell(ground, cell);
        Debug.Log($"[Potato Harvest] Ground='{ground.name}', Cell={cell}, HP=0, 획득=0개, 파헤쳐진 감자 (GD F). 괭이 좌클릭으로 복구", this);
    }

    private static bool TryGetCoveredGrowthState(PotatoGrowthStage stage, out GroundCellState state)
    {
        switch (stage)
        {
            case PotatoGrowthStage.TwoLargeLeaves:
                state = GroundCellState.SoilCoveredFirst;
                return true;
            case PotatoGrowthStage.FourLargeLeaves:
                state = GroundCellState.SoilCoveredSecond;
                return true;
            case PotatoGrowthStage.SixLargeLeaves:
                state = GroundCellState.SoilCoveredThird;
                return true;
            case PotatoGrowthStage.FreshSixLargeLeaves:
                state = GroundCellState.SoilCoveredFourth;
                return true;
            case PotatoGrowthStage.WitheredSixLeaves:
                state = GroundCellState.SoilCoveredFifth;
                return true;
            default:
                state = default;
                return false;
        }
    }

    private static bool IsForkDiscardState(GroundCellState state)
    {
        return state == GroundCellState.UncoveredMidEarly
            || state == GroundCellState.UncoveredLowEarly
            || state == GroundCellState.CoveredMidEarly
            || state == GroundCellState.CoveredLowEarly;
    }

    private void LogResult(PotatoHarvestResult result)
    {
        string timing = result.Timing switch
        {
            PotatoHarvestTiming.Early => "이른 수확",
            PotatoHarvestTiming.Optimal => "적기 수확",
            _ => "늦은 수확"
        };
        string quality = result.Quality == PotatoHarvestQuality.Normal ? "정상" : "상처 입은";
        if (result.Count == 0)
        {
            Debug.Log(
                $"[Potato Harvest] Cell={result.Cell}, 수확시기={timing}, 굴착상태={quality}, " +
                $"HP={result.Health:0.#}/{result.MaxHealth:0.#}, 수확개수=0, 결과=파헤쳐진 감자",
                this);
            return;
        }

        var itemNames = new string[result.Count];
        var starCounts = new int[4];
        for (int index = 0; index < result.Count; index++)
        {
            if (result.ItemConditions[index] != HarvestedPotatoCondition.None)
            {
                itemNames[index] = $"상태이상 감자 ({PotatoHarvestConditionRules.Describe(result.ItemConditions[index])})";
                continue;
            }
            starCounts[(int)result.Grades[index]]++;
            itemNames[index] = $"{(int)result.Grades[index]}성 감자";
        }
        Debug.Log(
            $"[Potato Harvest] Cell={result.Cell}, 수확시기={timing}, 굴착상태={quality}, " +
            $"HP={result.Health:0.#}/{result.MaxHealth:0.#}, 수확개수={result.Count}, " +
            $"획득: 1성 {starCounts[1]}개, 2성 {starCounts[2]}개, 3성 {starCounts[3]}개, " +
            $"정상 {result.Count - result.AbnormalCount}개, 상태이상 {result.AbnormalCount}개, 개별=[{string.Join(", ", itemNames)}]",
            this);
    }

    private void Unsubscribe()
    {
        if (!farmingSystem) return;
        farmingSystem.FarmingStageChanged -= OnFarmingStageChanged;
        farmingSystem.FarmingTileDied -= OnCropDied;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        harvestTimings.Clear();
        coveredCells.Clear();
        harvestConditions.Clear();
    }
}
