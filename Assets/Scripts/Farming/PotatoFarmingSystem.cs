using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Receives planting requests and forwards growth to the owning grid and cell.</summary>
[DisallowMultipleComponent]
public sealed class PotatoFarmingSystem : MonoBehaviour
{
    [SerializeField] private GroundGrid ground;
    [SerializeField] private WorldClock clock;

    [Serializable]
    public struct StageMapping
    {
        public PotatoGrowthStage stage;
        public GroundCellState cellState;
    }

    [Tooltip("Only mapped stages call TrySetCellState. Add mappings when the ground team adds the corresponding states.")]
    [SerializeField] private StageMapping[] stageMappings =
    {
        new StageMapping { stage = PotatoGrowthStage.SoilMound, cellState = GroundCellState.PotatoCovered }
    };

    public sealed class FarmingTile
    {
        public GroundGrid Ground { get; }
        public Vector2Int Cell { get; }
        public double PlantedAt { get; }
        public PotatoGrowthStage Stage { get; internal set; }
        private readonly WorldClock clock;
        public double AgeSeconds => clock ? Math.Max(0d, clock.ElapsedSeconds - PlantedAt) : 0d;
        public bool IsReadyToHarvest => AgeSeconds >= PotatoGrowthRules.BaseGrowthSeconds;
        public float Progress => (float)Math.Min(1d, AgeSeconds / PotatoGrowthRules.BaseGrowthSeconds);

        internal FarmingTile(GroundGrid ground, Vector2Int cell, WorldClock clock)
        {
            Ground = ground;
            Cell = cell;
            this.clock = clock;
            PlantedAt = clock.ElapsedSeconds;
            Stage = PotatoGrowthStage.SoilMound;
        }
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
        var tile = new FarmingTile(targetGround, cell, clock);
        farmingTiles.Add(key, tile);
        ForwardStage(tile, tile.Stage);
        return true;
    }

    public bool TryGetFarmingTile(GroundGrid targetGround, Vector2Int cell, out FarmingTile tile)
        => farmingTiles.TryGetValue((targetGround, cell), out tile);

    public bool TryGetFarmingTile(Vector2Int cell, out FarmingTile tile)
        => TryGetFarmingTile(ground, cell, out tile);

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
                if (tile.Stage != PotatoGrowthStage.WitheredSixLeaves) pendingGrowth.Add(tile);
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

