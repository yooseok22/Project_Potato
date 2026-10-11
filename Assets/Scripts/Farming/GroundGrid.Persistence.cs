using System;
using UnityEngine;

public sealed partial class GroundGrid
{
    internal FarmGridData CaptureSaveData()
    {
        var data = new FarmGridData { id = SaveIdentity.Key(this), width = Width, depth = Depth, cellSize = CellSize, origin = Origin };
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Depth; y++)
            {
                // Basic untouched soil is implicit. Modified empty fields are still persisted.
                if (cells[x, y] == GroundCellState.BasicSoil && soilCoverLayers[x, y] == 0 && !watered[x, y]) continue;
                data.cells.Add(new FarmCellData { x = x, y = y, state = cells[x, y], soilCoverLayers = soilCoverLayers[x, y], watered = watered[x, y] });
            }
        return data;
    }

    internal void ValidateSaveData(FarmGridData data)
    {
        SaveValidation.Require(data.width == Width && data.depth == Depth && data.cellSize == CellSize && data.origin.Equals(Origin),
            "Saved grid geometry does not match: " + data.id);
        SaveValidation.Require(data.cells != null, "Missing grid cells.");
        var seen = new System.Collections.Generic.HashSet<Vector2Int>();
        foreach (var cell in data.cells)
            SaveValidation.Require(cell != null && ContainsCell(new Vector2Int(cell.x, cell.y)) && seen.Add(new Vector2Int(cell.x, cell.y))
                && Enum.IsDefined(typeof(GroundCellState), cell.state), "Invalid or duplicate ground cell.");
    }

    internal void RestoreSaveData(FarmGridData data)
    {
        cells = new GroundCellState[Width, Depth];
        soilCoverLayers = new byte[Width, Depth];
        watered = new bool[Width, Depth];
        foreach (var cell in data.cells)
        {
            cells[cell.x, cell.y] = cell.state;
            soilCoverLayers[cell.x, cell.y] = cell.soilCoverLayers;
            watered[cell.x, cell.y] = cell.watered;
        }
    }

    internal void NotifySaveRestored()
    {
        // Notify only after clocks, crops and harvest bookkeeping have all been replaced.
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Depth; y++) CellChanged?.Invoke(new Vector2Int(x, y));
    }
}
