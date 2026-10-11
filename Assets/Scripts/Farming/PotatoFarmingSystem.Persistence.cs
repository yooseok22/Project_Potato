using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class PotatoFarmingSystem
{
    public sealed partial class FarmingTile
    {
        internal FarmCropData CaptureSaveData() => new()
        {
            gridId = SaveIdentity.Key(Ground), x = Cell.x, y = Cell.y, plantedAt = PlantedAt,
            growthStartedAt = GrowthStartedAt ?? 0, watered = IsWatered,
            growthPausedAt = GrowthPausedAt ?? 0, paused = IsGrowthPaused, totalPausedSeconds = TotalPausedSeconds,
            stage = Stage, health = CurrentHealth, maxHealth = MaxHealth
        };

        internal FarmingTile(FarmCropData data, GroundGrid grid, WorldClock clock, Action<FarmingTile, float> healthChanged)
        {
            Ground = grid;
            Cell = new Vector2Int(data.x, data.y);
            this.clock = clock;
            this.healthChanged = healthChanged;
            PlantedAt = data.plantedAt;
            GrowthStartedAt = data.watered ? data.growthStartedAt : (double?)null;
            GrowthPausedAt = data.paused ? data.growthPausedAt : (double?)null;
            TotalPausedSeconds = data.totalPausedSeconds;
            Stage = data.stage;
            CurrentHealth = data.health;
            MaxHealth = data.maxHealth;
        }
    }

    internal FarmSystemData CaptureSaveData()
    {
        var data = new FarmSystemData { id = SaveIdentity.Key(this), clockId = clock ? SaveIdentity.Key(clock) : null };
        foreach (var tile in farmingTiles.Values) data.crops.Add(tile.CaptureSaveData());
        return data;
    }

    internal void RestoreSaveData(FarmSystemData data, Dictionary<string, GroundGrid> grids, Dictionary<string, WorldClock> clocks)
    {
        Unsubscribe();
        clock = string.IsNullOrEmpty(data.clockId) ? null : clocks[data.clockId];
        farmingTiles.Clear();
        pendingGrowth.Clear();
        foreach (var crop in data.crops)
        {
            var grid = grids[crop.gridId];
            var tile = new FarmingTile(crop, grid, clock, OnHealthChanged);
            farmingTiles.Add((grid, tile.Cell), tile);
        }
        Subscribe();
        // Do not replay planting/stage transitions: those would overwrite soil-cover and cut visuals.
    }
}
