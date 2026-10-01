using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Receives planting requests and forwards growth to the owning grid and cell.</summary>
[DisallowMultipleComponent]
public sealed class PotatoFarmingSystem : MonoBehaviour
{
    [SerializeField] private GroundGrid ground;
    [SerializeField] private WorldClock clock;
    [SerializeField, Min(1f)] private float defaultMaxHealth = 100f;

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

    public sealed class FarmingTile : IDamageable
    {
        public GroundGrid Ground { get; }
        public Vector2Int Cell { get; }
        public double PlantedAt { get; }
        public double? GrowthStartedAt { get; internal set; }
        public bool IsWatered => GrowthStartedAt.HasValue;
        public PotatoGrowthStage Stage { get; internal set; }
        public float CurrentHealth { get; private set; }
        public float MaxHealth { get; }
        public bool IsDead => CurrentHealth <= 0f;
        private readonly WorldClock clock;
        private readonly Action<FarmingTile, float> healthChanged;
        /// <summary>Growth age, excluding the wait between planting and first watering.</summary>
        public double AgeSeconds => clock && GrowthStartedAt.HasValue
            ? Math.Max(0d, clock.ElapsedSeconds - GrowthStartedAt.Value) : 0d;
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
        if (stageMappings != null)
        {
            foreach (var mapping in stageMappings)
            {
                if (mapping.stage != stage) continue;
                // false also means "already in this state"; the registration is still valid.
                tile.Ground.TrySetCellState(tile.Cell, mapping.cellState);
                break;
            }
        }
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
                if (tile.IsWatered && !tile.IsDead && tile.Stage != PotatoGrowthStage.WitheredSixLeaves)
                    pendingGrowth.Add(tile);
            foreach (var tile in pendingGrowth)
            {
                var key = (tile.Ground, tile.Cell);
                if (!tile.Ground) { farmingTiles.Remove(key); continue; }
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

