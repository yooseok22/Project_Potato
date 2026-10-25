using System.Collections.Generic;
using UnityEngine;

public sealed partial class PotatoHarvestCoordinator
{
    internal FarmHarvestData CaptureSaveData()
    {
        var data = new FarmHarvestData { id = SaveIdentity.Key(this), systemId = farmingSystem ? SaveIdentity.Key(farmingSystem) : null };
        var keys = new HashSet<(GroundGrid, Vector2Int)>(coveredCells);
        keys.UnionWith(harvestTimings.Keys);
        keys.UnionWith(harvestConditions.Keys);
        foreach (var key in keys)
        {
            bool cut = harvestTimings.TryGetValue(key, out var timing);
            harvestConditions.TryGetValue(key, out var conditions);
            data.cells.Add(new FarmHarvestCellData { gridId = SaveIdentity.Key(key.Item1), x = key.Item2.x, y = key.Item2.y,
                covered = coveredCells.Contains(key), cut = cut, timing = timing, conditions = conditions });
        }
        return data;
    }

    internal void RestoreSaveData(FarmHarvestData data, Dictionary<string, GroundGrid> grids, Dictionary<string, PotatoFarmingSystem> systems)
    {
        Configure(string.IsNullOrEmpty(data.systemId) ? null : systems[data.systemId]);
        harvestTimings.Clear();
        coveredCells.Clear();
        harvestConditions.Clear();
        foreach (var cell in data.cells)
        {
            var key = (grids[cell.gridId], new Vector2Int(cell.x, cell.y));
            if (cell.cut) harvestTimings.Add(key, cell.timing);
            if (cell.covered) coveredCells.Add(key);
            if (cell.conditions != HarvestedPotatoCondition.None) harvestConditions.Add(key, cell.conditions);
        }
    }
}
