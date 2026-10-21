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
    public bool HasAbnormalStatus => Status != PotatoStatus.None;
    public PotatoHarvestGrade[] Grades { get; }
    public int Count => Grades?.Length ?? 0;

    public PotatoHarvestResult(GroundGrid ground, Vector2Int cell, PotatoHarvestTiming timing,
        PotatoHarvestQuality quality, string stateCode, float health, float maxHealth,
        PotatoHarvestGrade[] grades, PotatoStatus status = PotatoStatus.None)
    {
        Ground = ground;
        Cell = cell;
        Timing = timing;
        Quality = quality;
        StateCode = stateCode;
        Health = health;
        MaxHealth = maxHealth;
        Status = status;
        Grades = grades ?? Array.Empty<PotatoHarvestGrade>();
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

    public static PotatoHarvestGrade[] Roll(float health)
    {
        GetCountRange(health, out int minimum, out int maximum);
        int count = minimum == maximum ? minimum : UnityEngine.Random.Range(minimum, maximum + 1);
        var grades = new PotatoHarvestGrade[count];
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
    private PotatoFarmingSystem farmingSystem;

    public void Configure(PotatoFarmingSystem system)
    {
        if (farmingSystem == system) return;

        if (farmingSystem) farmingSystem.FarmingStageChanged -= OnFarmingStageChanged;
        farmingSystem = system;
        if (farmingSystem) farmingSystem.FarmingStageChanged += OnFarmingStageChanged;
    }

    public void ResetCell(GroundGrid ground, Vector2Int cell)
    {
        harvestTimings.Remove((ground, cell));
        coveredCells.Remove((ground, cell));
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
        return tile.Stage == PotatoGrowthStage.WitheredSixLeaves
            || tile.IsWatered && !tile.IsGrowthPaused && !tile.IsDead;
    }

    public bool TryCutForHarvest(GroundGrid ground, Vector2Int cell)
    {
        if (!CanCutForHarvest(ground, cell)) return false;

        GroundCellState state = ground.GetCellState(cell);
        if (!TryGetHarvestTiming(state, out PotatoHarvestTiming timing, out bool covered)) return false;
        if (!farmingSystem.TryGetFarmingTile(ground, cell, out var tile)) return false;
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
        return true;
    }

    public bool TryUsePotatoFork(GroundGrid ground, Vector2Int cell)
    {
        if (!ground || !ground.CanUsePotatoFork(cell)) return false;
        GroundCellState state = ground.GetCellState(cell);
        bool discardsCrop = IsForkDiscardState(state);
        if (discardsCrop && (!farmingSystem || !farmingSystem.StopFarming(ground, cell))) return false;
        if (!ground.TryUsePotatoFork(cell)) return false;
        if (discardsCrop) ResetCell(ground, cell);
        return true;
    }

    public bool CanHarvest(GroundGrid ground, Vector2Int cell)
    {
        return farmingSystem && ground && harvestTimings.ContainsKey((ground, cell))
            && ground.CanHarvestPotato(cell)
            && farmingSystem.TryGetFarmingTile(ground, cell, out _);
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
        if (!ground.TrySetCellState(cell, GroundCellState.PlantingHole))
        {
            Debug.LogError($"[Potato Harvest] Farming data completed but G_001 transition failed. Cell={cell}", this);
            return false;
        }

        var grades = PotatoHarvestRewardRules.Roll(health);
        result = new PotatoHarvestResult(ground, cell, timing, quality, stateCode,
            health, maximumHealth, grades, tile.Status |
                (quality == PotatoHarvestQuality.Damaged ? PotatoStatus.Injured : PotatoStatus.None));
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
        if (!targetGround || !coveredCells.Contains((targetGround, cell))
            || !TryGetCoveredGrowthState(stage, out GroundCellState state)) return;
        targetGround.TrySetCellState(cell, state);
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
            || state == GroundCellState.UncoveredLowYield
            || state == GroundCellState.CoveredMidEarly
            || state == GroundCellState.CoveredLowEarly
            || state == GroundCellState.CoveredLowYield;
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
                $"[Potato Harvest] Cell={result.Cell}, 수확시기={timing}, 감자상태={quality}, " +
                $"HP={result.Health:0.#}/{result.MaxHealth:0.#}, 수확개수=0, 결과=파헤쳐진 감자",
                this);
            return;
        }

        var gradeNames = new string[result.Count];
        for (int index = 0; index < result.Count; index++)
            gradeNames[index] = $"{(int)result.Grades[index]}성";
        Debug.Log(
            $"[Potato Harvest] Cell={result.Cell}, 수확시기={timing}, 감자상태={quality}, " +
            $"HP={result.Health:0.#}/{result.MaxHealth:0.#}, 수확개수={result.Count}, " +
            $"등급=[{string.Join(", ", gradeNames)}]",
            this);
    }

    private void OnDestroy()
    {
        if (farmingSystem) farmingSystem.FarmingStageChanged -= OnFarmingStageChanged;
        harvestTimings.Clear();
        coveredCells.Clear();
    }
}
