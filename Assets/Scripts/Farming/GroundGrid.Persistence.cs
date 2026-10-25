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
                data.cells.Add(new FarmCellData { x = x, y = y, state = cells[x, y], soilCoverLayers = soilCoverLayers[x, y], watered = watered[x, y], exposureDepth = exposureDepths[x, y] });
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
                && cell.exposureDepth <= 3 && Enum.IsDefined(typeof(GroundCellState), cell.state), "Invalid or duplicate ground cell.");
    }

    internal void RestoreSaveData(FarmGridData data)
    {
        cells = new GroundCellState[Width, Depth];
        soilCoverLayers = new byte[Width, Depth];
        watered = new bool[Width, Depth];
        exposureDepths = new byte[Width, Depth];
        foreach (var cell in data.cells)
        {
            cells[cell.x, cell.y] = cell.state;
            soilCoverLayers[cell.x, cell.y] = cell.soilCoverLayers;
            watered[cell.x, cell.y] = cell.watered;
            exposureDepths[cell.x, cell.y] = !HasPotatoState(cell.state) ? (byte)0 :
                cell.exposureDepth > 0 ? cell.exposureDepth : InferLegacyExposureDepth(cell);
        }
    }

    private static byte InferLegacyExposureDepth(FarmCellData cell)
    {
        // Existing saves have no depth field; preserve visible/hidden digging progress.
        switch (cell.state)
        {
            case GroundCellState.UncoveredHighYield: case GroundCellState.CoveredHighYield:
            case GroundCellState.UncoveredMidEarly: case GroundCellState.CoveredMidEarly: return 1;
            case GroundCellState.UncoveredMidYield: case GroundCellState.CoveredMidYield:
            case GroundCellState.UncoveredLowEarly: case GroundCellState.CoveredLowEarly: return 2;
            case GroundCellState.UncoveredLowYield: case GroundCellState.CoveredLowYield:
            case GroundCellState.UncoveredMidHidden: case GroundCellState.CoveredMidHidden: return 3;
            case GroundCellState.UncoveredHighHidden: case GroundCellState.CoveredHighHidden: return 2;
            default: return (byte)(1 + (cell.x * 73 + cell.y * 37) % 3);
        }
    }

    internal void NotifySaveRestored()
    {
        // Notify only after clocks, crops and harvest bookkeeping have all been replaced.
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Depth; y++) CellChanged?.Invoke(new Vector2Int(x, y));
    }
}
