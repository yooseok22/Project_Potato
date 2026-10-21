using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum PotatoStatus
{
    None = 0,
    Green = 1,
    BugEaten = 2,
    Injured = 4
}

/// <summary>Receives planting requests and forwards growth to the owning grid and cell.</summary>
[DisallowMultipleComponent]
public sealed partial class PotatoFarmingSystem : MonoBehaviour
{
    [SerializeField] private GroundGrid ground;
    [SerializeField] private WorldClock clock;
    [SerializeField, Min(1f)] private float defaultMaxHealth = 100f;
    [SerializeField, Range(0f, 1f)] private float bugSpawnChance = 0.30f;
    public const double BugLifetimeSeconds = 150d;

    [Serializable]
    public struct StageMapping
    {
        public PotatoGrowthStage stage;
        public GroundCellState cellState;
    }

    [Tooltip("Only mapped stages call TrySetCellState. Add mappings when the ground team adds the corresponding states.")]
    [SerializeField] private StageMapping[] stageMappings =
    {
        new StageMapping { stage = PotatoGrowthStage.SoilMound, cellState = GroundCellState.PotatoCovered },
        new StageMapping { stage = PotatoGrowthStage.Sprout, cellState = GroundCellState.SproutFirst },
        new StageMapping { stage = PotatoGrowthStage.TwoLargeLeaves, cellState = GroundCellState.SproutSecond },
        new StageMapping { stage = PotatoGrowthStage.FourLargeLeaves, cellState = GroundCellState.SproutThird },
        new StageMapping { stage = PotatoGrowthStage.SixLargeLeaves, cellState = GroundCellState.EarlyHarvest },
        new StageMapping { stage = PotatoGrowthStage.FreshSixLargeLeaves, cellState = GroundCellState.ReadyToHarvest },
        new StageMapping { stage = PotatoGrowthStage.WitheredSixLeaves, cellState = GroundCellState.LateHarvest }
    };

    public sealed partial class FarmingTile : IDamageable
    {
        public GroundGrid Ground { get; }
        public Vector2Int Cell { get; }
        public double PlantedAt { get; }
        public double? GrowthStartedAt { get; internal set; }
        public bool IsWatered => GrowthStartedAt.HasValue;
        public bool IsGrowthPaused => GrowthPausedAt.HasValue;
        public PotatoGrowthStage Stage { get; internal set; }
        public float CurrentHealth { get; private set; }
        public float MaxHealth { get; }
        public bool IsDead => CurrentHealth <= 0f;
        public PotatoStatus Status { get; internal set; }
        public bool HasAbnormalStatus => Status != PotatoStatus.None;
        public bool IsSoilCovered { get; internal set; }
        public bool IsMulchingFilmApplied { get; internal set; }
        internal bool SoilCoverDeadlineChecked { get; set; }
        public bool HasBugs { get; internal set; }
        public double BugSecondsRemaining => HasBugs && clock
            ? Math.Max(0d, BugExpiresAt - clock.ElapsedSeconds) : 0d;
        internal bool BugSpawnChecked { get; set; }
        internal double BugExpiresAt { get; set; }
        private double? GrowthPausedAt { get; set; }
        private double TotalPausedSeconds { get; set; }
        private readonly WorldClock clock;
        private readonly Action<FarmingTile, float> healthChanged;
        /// <summary>Growth age, excluding the wait between planting and first watering.</summary>
        public double AgeSeconds => clock && GrowthStartedAt.HasValue
            ? Math.Max(0d, (GrowthPausedAt ?? clock.ElapsedSeconds) -
                GrowthStartedAt.Value - TotalPausedSeconds) : 0d;
        public bool IsReadyToHarvest => AgeSeconds >= PotatoGrowthRules.ReadyToHarvestSeconds;
        public float Progress => (float)Math.Min(1d, AgeSeconds / PotatoGrowthRules.BaseGrowthSeconds);

        internal FarmingTile(GroundGrid ground, Vector2Int cell, WorldClock clock,
            float maxHealth, Action<FarmingTile, float> healthChanged)
        {
            Ground = ground;
            Cell = cell;
            this.clock = clock;
            PlantedAt = clock.ElapsedSeconds;
            Stage = PotatoGrowthStage.SoilMound;
            MaxHealth = Mathf.Max(1f, maxHealth);
            CurrentHealth = MaxHealth;
            this.healthChanged = healthChanged;
        }

        internal bool TryPauseGrowth()
        {
            if (!clock || !IsWatered || IsGrowthPaused || IsDead ||
                Stage == PotatoGrowthStage.WitheredSixLeaves) return false;

            GrowthPausedAt = clock.ElapsedSeconds;
            return true;
        }

        internal bool TryResumeGrowth()
        {
            if (!clock || !GrowthPausedAt.HasValue) return false;

            TotalPausedSeconds += Math.Max(0d, clock.ElapsedSeconds - GrowthPausedAt.Value);
            GrowthPausedAt = null;
            return true;
        }

        // Damage is applied through PotatoFarmingSystem.TryDamage so callers only need
        // the owning ground, the affected cell, and the damage amount.
        bool IDamageable.TryTakeDamage(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f || IsDead) return false;
            float previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            healthChanged?.Invoke(this, previousHealth - CurrentHealth);
            return true;
        }

        /// <summary>Returns this tile crop's current HP for harvest-yield calculation.</summary>
        public float returnPotatoHp() => CurrentHealth;
    }

    private readonly Dictionary<(GroundGrid, Vector2Int), FarmingTile> farmingTiles = new();
    private readonly List<FarmingTile> pendingGrowth = new();
    private WorldClock subscribedClock;
    private bool refreshing;
    public int FarmingCount => farmingTiles.Count;
    // Snapshot: listeners may start/stop farming while iterating without invalidating the collection.
    public FarmingTile[] GetFarmingTiles()
    {
        var result = new FarmingTile[farmingTiles.Count];
        farmingTiles.Values.CopyTo(result, 0);
        return result;
    }

    /// <summary>Includes unmapped stages, so consumers always receive exact growth information.</summary>
    public event Action<GroundGrid, Vector2Int, PotatoGrowthStage> FarmingStageChanged;
    /// <summary>Raised with the tile, damage actually applied, and resulting health.</summary>
    public event Action<GroundGrid, Vector2Int, float, float> FarmingHealthChanged;
    public event Action<GroundGrid, Vector2Int> FarmingTileDied;
    /// <summary>True when bugs appear; false when chased away or their deadline expires.</summary>
    public event Action<GroundGrid, Vector2Int, bool> FarmingBugsChanged;

    /// <summary>Convenience entry point for interactions using the Inspector-assigned grid and clock.</summary>
    public bool TryStartFarming(Vector2Int cell) => TryStartFarming(ground, cell, clock);
    public bool TryStartFarming(GroundGrid targetGround, Vector2Int cell) => TryStartFarming(targetGround, cell, clock);

    /// <summary>
    /// Call after the planting interaction succeeds. Does not decide tool/soil/inventory requirements.
    /// Returns false for missing dependencies, out-of-bounds cells, or duplicate planting.
    /// One system should own planting registrations for a given grid.
    /// </summary>
    public bool TryStartFarming(GroundGrid targetGround, Vector2Int cell, WorldClock worldClock)
    {
        if (!targetGround || !worldClock || !targetGround.ContainsCell(cell)) return false;
        var key = (targetGround, cell);
        if (farmingTiles.ContainsKey(key)) return false;
        // All registrations in this system share one simulation clock.
        if (clock && clock != worldClock) return false;
        clock = worldClock;
        Subscribe();
        var tile = new FarmingTile(targetGround, cell, clock, defaultMaxHealth, OnHealthChanged);
        farmingTiles.Add(key, tile);
        ForwardStage(tile, tile.Stage);
        return true;
    }

    public bool TryGetFarmingTile(GroundGrid targetGround, Vector2Int cell, out FarmingTile tile)
        => farmingTiles.TryGetValue((targetGround, cell), out tile);

    public bool TryGetFarmingTile(Vector2Int cell, out FarmingTile tile)
        => TryGetFarmingTile(ground, cell, out tile);

    public bool TryApplyMulchingFilm(Vector2Int cell) => TryApplyMulchingFilm(ground, cell);

    /// <summary>Call after film application succeeds. Inventory and player interaction belong to the caller.
    /// Film prevents exposure for this crop's lifetime; it does not cure an existing Green status.</summary>
    public bool TryApplyMulchingFilm(GroundGrid targetGround, Vector2Int cell)
    {
        if (!targetGround || !TryGetFarmingTile(targetGround, cell, out var tile) ||
            tile.IsDead || tile.IsGrowthPaused || tile.IsMulchingFilmApplied ||
            tile.Stage == PotatoGrowthStage.WitheredSixLeaves) return false;
        tile.IsMulchingFilmApplied = true;
        ApplyMulchingVisual(tile, tile.Stage);
        return true;
    }

    private static void ApplyMulchingVisual(FarmingTile tile, PotatoGrowthStage stage)
    {
        if (!tile.IsMulchingFilmApplied || stage < PotatoGrowthStage.TwoLargeLeaves ||
            stage > PotatoGrowthStage.WitheredSixLeaves) return;
        tile.Ground.TrySetCellState(tile.Cell, (GroundCellState)((int)GroundCellState.SoilCoveredFirst +
            (int)stage - (int)PotatoGrowthStage.TwoLargeLeaves));
    }

    public bool TryRemoveBugs(Vector2Int cell) => TryRemoveBugs(ground, cell);

    /// <summary>Shared soil-cover operation for helpers and other non-player callers.</summary>
    public bool TryCoverSoil(GroundGrid targetGround, Vector2Int cell)
    {
        if (!targetGround || !TryGetFarmingTile(targetGround, cell, out var tile) || tile.IsDead ||
            tile.IsGrowthPaused || tile.IsMulchingFilmApplied || !targetGround.TryCoverPotato(cell)) return false;
        tile.IsSoilCovered = true;
        return true;
    }

    /// <summary>Call after the player's chase interaction. At/after the deadline, damage wins.
    /// Returns true only when living bugs were removed before expiry; never cures BugEaten.</summary>
    public bool TryRemoveBugs(GroundGrid targetGround, Vector2Int cell)
    {
        if (!targetGround || !TryGetFarmingTile(targetGround, cell, out var tile) || tile.IsDead) return false;
        RefreshBugDeadline(tile);
        if (!tile.HasBugs) return false;
        tile.HasBugs = false;
        FarmingBugsChanged?.Invoke(targetGround, cell, false);
        return true;
    }

    private void RefreshBugDeadline(FarmingTile tile)
    {
        if (!tile.HasBugs || !clock || clock.ElapsedSeconds < tile.BugExpiresAt) return;
        tile.HasBugs = false;
        tile.Status |= PotatoStatus.BugEaten;
        TryDamage(tile.Ground, tile.Cell, 10f);
        FarmingBugsChanged?.Invoke(tile.Ground, tile.Cell, false);
    }

    /// <summary>Records a condition independently of HP and reward selection. Conditions can coexist.</summary>
    public bool TryAddStatus(GroundGrid targetGround, Vector2Int cell, PotatoStatus status)
    {
        const PotatoStatus supported = PotatoStatus.Green | PotatoStatus.BugEaten | PotatoStatus.Injured;
        if (status == PotatoStatus.None || (status & ~supported) != 0 ||
            !TryGetFarmingTile(targetGround, cell, out var tile)) return false;
        var next = tile.Status | status;
        if (next == tile.Status) return false;
        tile.Status = next;
        return true;
    }

    /// <summary>Notify after watering succeeds. First watering starts growth; repeats never reset time.
    /// Does not perform the watering interaction or modify GroundGrid's water state.</summary>
    public bool TryWater(Vector2Int cell) => TryWater(ground, cell);

    public bool TryWater(GroundGrid targetGround, Vector2Int cell)
    {
        if (!targetGround || !clock || !targetGround.ContainsCell(cell) ||
            !farmingTiles.TryGetValue((targetGround, cell), out var tile) || tile.IsWatered || tile.IsDead) return false;
        tile.GrowthStartedAt = clock.ElapsedSeconds;
        return true;
    }

    /// <summary>Pauses growth time for the crop at the default grid coordinate.</summary>
    public bool TryPauseGrowth(Vector2Int cell) => TryPauseGrowth(ground, cell);

    /// <summary>
    /// Pauses only the crop registered at this coordinate. World time and other crops keep advancing.
    /// Returns false when no actively growing crop exists at the coordinate or it is already paused.
    /// </summary>
    public bool TryPauseGrowth(GroundGrid targetGround, Vector2Int cell)
    {
        return targetGround && targetGround.ContainsCell(cell) &&
            farmingTiles.TryGetValue((targetGround, cell), out var tile) && tile.TryPauseGrowth();
    }

    /// <summary>Resumes growth time for the crop at the default grid coordinate.</summary>
    public bool TryResumeGrowth(Vector2Int cell) => TryResumeGrowth(ground, cell);

    /// <summary>
    /// Resumes a paused crop without counting the time spent paused toward its growth age.
    /// </summary>
    public bool TryResumeGrowth(GroundGrid targetGround, Vector2Int cell)
    {
        return targetGround && targetGround.ContainsCell(cell) &&
            farmingTiles.TryGetValue((targetGround, cell), out var tile) && tile.TryResumeGrowth();
    }

    /// <summary>
    /// Applies damage to the registered crop at the default grid coordinate.
    /// External damage sources should use this coordinate-based entry point instead of retrieving FarmingTile.
    /// </summary>
    public bool TryDamage(Vector2Int cell, float amount) => TryDamage(ground, cell, amount);

    /// <summary>
    /// Finds the crop registered to the ground cell and applies damage through its damage contract.
    /// Returns false when the ground/cell is invalid, no crop is registered, or damage cannot be applied.
    /// </summary>
    public bool TryDamage(GroundGrid targetGround, Vector2Int cell, float amount)
    {
        if (!targetGround || !targetGround.ContainsCell(cell) ||
            !farmingTiles.TryGetValue((targetGround, cell), out var tile)) return false;

        IDamageable damageable = tile;
        return damageable.TryTakeDamage(amount);
    }

    /// <summary>
    /// Returns the crop's current HP and removes its farming data in one operation.
    /// Harvest requirements and tile visuals remain the caller's responsibility.
    /// </summary>
    public bool TryCompleteHarvest(Vector2Int cell, out float potatoHp)
        => TryCompleteHarvest(ground, cell, out potatoHp);

    public bool TryCompleteHarvest(GroundGrid targetGround, Vector2Int cell, out float potatoHp)
    {
        potatoHp = 0f;
        var key = (targetGround, cell);
        if (!targetGround || !targetGround.ContainsCell(cell) ||
            !farmingTiles.TryGetValue(key, out var tile) || !farmingTiles.Remove(key)) return false;
        potatoHp = tile.returnPotatoHp();
        return true;
    }

    /// <summary>Unregister after harvest/removal. The caller owns the resulting tile state.</summary>
    public bool StopFarming(GroundGrid targetGround, Vector2Int cell)
    {
        return farmingTiles.Remove((targetGround, cell));
    }

    public bool StopFarming(Vector2Int cell) => StopFarming(ground, cell);

    private void ForwardStage(FarmingTile tile, PotatoGrowthStage stage)
    {
        if (!tile.Ground) return;
        if (stage == PotatoGrowthStage.TwoLargeLeaves && !tile.BugSpawnChecked)
        {
            tile.BugSpawnChecked = true;
            if (UnityEngine.Random.value < bugSpawnChance)
            {
                tile.HasBugs = true;
                // A time skip must not grant another 150 seconds from the end of the skip.
                tile.BugExpiresAt = clock.ElapsedSeconds - Math.Max(0d, tile.AgeSeconds - 80d) + BugLifetimeSeconds;
                RefreshBugDeadline(tile);
                if (tile.HasBugs) FarmingBugsChanged?.Invoke(tile.Ground, tile.Cell, true);
            }
        }
        // Check before changing visuals, which otherwise replaces the covered tile state.
        tile.IsSoilCovered |= tile.Ground.IsSoilCoveredPotato(tile.Cell);
        if (stage >= PotatoGrowthStage.SixLargeLeaves && !tile.SoilCoverDeadlineChecked)
        {
            tile.SoilCoverDeadlineChecked = true;
            if (!tile.IsSoilCovered && !tile.IsMulchingFilmApplied)
            {
                tile.Status |= PotatoStatus.Green;
                TryDamage(tile.Ground, tile.Cell, 10f);
            }
        }
        if (stageMappings != null)
        {
            foreach (var mapping in stageMappings)
            {
                if (mapping.stage != stage) continue;
                // false also means "already in this state"; the registration is still valid.
                var mappedState = tile.IsSoilCovered && stage >= PotatoGrowthStage.TwoLargeLeaves
                    ? (GroundCellState)((int)GroundCellState.SoilCoveredFirst + (int)stage - (int)PotatoGrowthStage.TwoLargeLeaves)
                    : mapping.cellState;
                tile.Ground.TrySetCellState(tile.Cell, mappedState);
                break;
            }
        }
        ApplyMulchingVisual(tile, stage);
        FarmingStageChanged?.Invoke(tile.Ground, tile.Cell, stage);
    }

    private void OnHealthChanged(FarmingTile tile, float appliedDamage)
    {
        FarmingHealthChanged?.Invoke(tile.Ground, tile.Cell, appliedDamage, tile.CurrentHealth);
        if (tile.IsDead) FarmingTileDied?.Invoke(tile.Ground, tile.Cell);
    }

    private void OnEnable()
    {
        Subscribe();
        RefreshGrowth();
    }

    private void Subscribe()
    {
        if (!isActiveAndEnabled || !clock || subscribedClock == clock) return;
        Unsubscribe();
        subscribedClock = clock;
        subscribedClock.TimeAdvanced += RefreshGrowth;
    }

    private void Unsubscribe()
    {
        if (subscribedClock) subscribedClock.TimeAdvanced -= RefreshGrowth;
        subscribedClock = null;
    }

    /// <summary>One central pass. Snapshot tolerates registration/removal inside stage callbacks.</summary>
    public void RefreshGrowth()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            pendingGrowth.Clear();
            foreach (var tile in farmingTiles.Values)
                if (!tile.IsDead && (tile.HasBugs || tile.IsWatered && !tile.IsGrowthPaused &&
                    tile.Stage != PotatoGrowthStage.WitheredSixLeaves))
                    pendingGrowth.Add(tile);
            foreach (var tile in pendingGrowth)
            {
                var key = (tile.Ground, tile.Cell);
                if (!tile.Ground) { farmingTiles.Remove(key); continue; }
                RefreshBugDeadline(tile);
                if (tile.IsDead || tile.IsGrowthPaused || !tile.IsWatered ||
                    !farmingTiles.TryGetValue(key, out var registered) || !ReferenceEquals(registered, tile)) continue;
                var target = PotatoGrowthRules.StageAt(tile.AgeSeconds);
                while (tile.Stage < target && farmingTiles.TryGetValue(key, out var current) && ReferenceEquals(current, tile))
                {
                    tile.Stage = (PotatoGrowthStage)((int)tile.Stage + 1);
                    ForwardStage(tile, tile.Stage);
                }
            }
        }
        finally { pendingGrowth.Clear(); refreshing = false; }
    }

    private void OnDisable() => Unsubscribe();

    private void OnDestroy()
    {
        Unsubscribe();
        farmingTiles.Clear();
    }
}

