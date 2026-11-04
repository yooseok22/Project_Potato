using System;
using UnityEngine;

public sealed partial class PotatoFarmingSystem
{
    public sealed partial class FarmingTile
    {
        public double BugPreventionUntil { get; internal set; }
        public bool HasBugPrevention => clock && BugPreventionUntil > clock.ElapsedSeconds;
    }

    public bool CanRobotPlant(GroundGrid grid, Vector2Int cell)
    {
        if (!clock || !grid || !grid.ContainsCell(cell) || farmingTiles.ContainsKey((grid, cell))) return false;
        var state = grid.GetCellState(cell);
        return state == GroundCellState.BasicSoil || state == GroundCellState.FieldSoil || state == GroundCellState.PlantingHole ||
            state == GroundCellState.BoarDugField || state == GroundCellState.HarvestClearedField;
    }

    public bool TryRobotPlant(GroundGrid grid, Vector2Int cell)
    {
        if (!CanRobotPlant(grid, cell)) return false;
        var previous = grid.GetCellState(cell);
        grid.TrySetCellState(cell, GroundCellState.FieldSoil);
        grid.TrySetCellState(cell, GroundCellState.PlantingHole);
        grid.TryPlantPotato(cell);
        if (!TryStartFarming(grid, cell)) { grid.TrySetCellState(cell, previous); return false; }
        // Prepare the same eight neighbors required by manual hole digging.
        // Only untouched soil changes; nearby crops and other prepared states stay intact.
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                var neighbor = cell + new Vector2Int(x, y);
                if (grid.CanDig(neighbor)) grid.TryDig(neighbor);
            }
        foreach (var harvest in FindObjectsByType<PotatoHarvestCoordinator>()) harvest.ResetCell(grid, cell);
        return true;
    }

    public bool CanRobotMulch(GroundGrid grid, Vector2Int cell) => TryGetFarmingTile(grid, cell, out var tile) &&
        !tile.IsDead && !tile.IsGrowthPaused && !tile.IsMulchingFilmApplied && tile.Stage <= PotatoGrowthStage.Sprout;

    public bool CanPreventBugs(GroundGrid grid, Vector2Int cell) => TryGetFarmingTile(grid, cell, out var tile) &&
        !tile.IsDead && !tile.HasBugs && !tile.HasBugPrevention && !tile.BugSpawnChecked;

    public bool TryPreventBugs(GroundGrid grid, Vector2Int cell, float seconds)
    {
        if (!clock || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || !CanPreventBugs(grid, cell)) return false;
        farmingTiles[(grid, cell)].BugPreventionUntil = clock.ElapsedSeconds + seconds;
        return true;
    }
}
