using System;
using UnityEngine;

public enum GroundCellState : byte
{
    BasicSoil = 0,
    FieldSoil = 1,
    PlantingHole = 15,       // G_001
    PotatoCovered = 16,      // G_002
    SproutFirst = 17,        // G_003
    SproutSecond = 18,       // GB_001
    SproutThird = 19,        // GB_002
    EarlyHarvest = 20,       // GB_003
    ReadyToHarvest = 21,     // GB_004
    LateHarvest = 22,        // GB_005
    SoilCoveredFirst = 23,   // GN_001
    SoilCoveredSecond = 24,  // GN_002
    SoilCoveredThird = 25,   // GN_003
    SoilCoveredFourth = 26,  // GN_004
    SoilCoveredFifth = 27,   // GN_005
    UncoveredCut = 28,       // GB F
    UncoveredHighYield = 29, // GB F HY
    UncoveredHighHidden = 30,// GB F HN
    UncoveredMidEarly = 31,  // GB F ME
    UncoveredMidYield = 32,  // GB F MY
    UncoveredMidHidden = 33, // GB F MN
    UncoveredLowEarly = 34,  // GB F LE
    UncoveredLowYield = 35,  // GB F LY
    CoveredCut = 36,         // GN F
    CoveredHighYield = 37,   // GN F HY
    CoveredHighHidden = 38,  // GN F HN
    CoveredMidEarly = 39,    // GN F ME
    CoveredMidYield = 40,    // GN F MY
    CoveredMidHidden = 41,   // GN F MN
    CoveredLowEarly = 42,    // GN F LE
    CoveredLowYield = 43,    // GN F LY
    BoarDugField = 44        // GD F
}

public enum PotatoHarvestQuality : byte
{
    Normal,
    Damaged
}

public sealed partial class GroundGrid : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float cellSize = 2f;
    [SerializeField, Min(1)] private int width = 50;
    [SerializeField, Min(1)] private int depth = 50;
    [Tooltip("World-space minimum corner of the grid; Y is the selection surface height.")]
    [SerializeField] private Vector3 origin = new Vector3(-50f, 0f, -50f);
    private GroundCellState[,] cells;
    private byte[,] soilCoverLayers;
    private bool[,] watered;
    public event Action<Vector2Int> CellChanged;
    public float CellSize => Mathf.Max(0.01f, cellSize);
    public int Width => Mathf.Max(1, width);
    public int Depth => Mathf.Max(1, depth);
    public Vector3 Origin => origin;

    private void Awake()
    {
        cells = new GroundCellState[Width, Depth];
        soilCoverLayers = new byte[Width, Depth];
        watered = new bool[Width, Depth];
    }

    public Vector2Int WorldToCell(Vector3 point) => new Vector2Int(
        Mathf.FloorToInt((point.x - Origin.x) / CellSize),
        Mathf.FloorToInt((point.z - Origin.z) / CellSize));

    public Vector3 CellCenter(Vector2Int cell) => Origin + new Vector3(
        (cell.x + 0.5f) * CellSize, 0f, (cell.y + 0.5f) * CellSize);

    public bool ContainsCell(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Depth;
    public GroundCellState GetCellState(Vector2Int cell) => ContainsCell(cell)
        ? cells[cell.x, cell.y] : GroundCellState.BasicSoil;
    public int GetSoilCoverLayers(Vector2Int cell) => ContainsCell(cell) ? soilCoverLayers[cell.x, cell.y] : 0;
    public bool HasPotato(Vector2Int cell)
    {
        var state = GetCellState(cell);
        return HasPotatoState(state);
    }
    public bool CanPlant(Vector2Int cell) => GetCellState(cell) == GroundCellState.PlantingHole;
    public bool IsWatered(Vector2Int cell) => ContainsCell(cell) && watered[cell.x, cell.y];
    public bool CanWater(Vector2Int cell) => HasPotato(cell) && !IsWatered(cell);
    public bool IsDug(Vector2Int cell) => ContainsCell(cell) && cells[cell.x, cell.y] != GroundCellState.BasicSoil;
    public bool CanDig(Vector2Int cell) => ContainsCell(cell) && cells[cell.x, cell.y] == GroundCellState.BasicSoil;
    public bool CanFill(Vector2Int cell) => ContainsCell(cell) && cells[cell.x, cell.y] != GroundCellState.BasicSoil;
    public bool TryDig(Vector2Int cell) => TrySetCellState(cell, GroundCellState.FieldSoil);
    public bool TryFill(Vector2Int cell) => TrySetCellState(cell, GroundCellState.BasicSoil);

    public bool CanUseHoeSecondary(Vector2Int cell)
    {
        var state = GetCellState(cell);
        return state == GroundCellState.BasicSoil || CanCreatePlantingHole(cell);
    }
    public bool CanUseHoePrimary(Vector2Int cell)
    {
        return CanCreatePlantingHole(cell) || HasPotato(cell);
    }

    public bool CanCreatePlantingHole(Vector2Int cell)
    {
        if (!ContainsCell(cell) || GetCellState(cell) != GroundCellState.FieldSoil) return false;
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                var neighbor = cell + new Vector2Int(x, y);
                if (!ContainsCell(neighbor) || !IsPreparedFarmState(GetCellState(neighbor))) return false;
            }
        return true;
    }

    public bool TryUseHoeSecondary(Vector2Int cell)
    {
        if (!CanUseHoeSecondary(cell)) return false;
        switch (GetCellState(cell))
        {
            case GroundCellState.BasicSoil:
                return TrySetCellState(cell, GroundCellState.FieldSoil);
            case GroundCellState.FieldSoil:
                return TrySetCellState(cell, GroundCellState.PlantingHole);
            default:
                return false;
        }
    }

    public bool TryUseHoePrimary(Vector2Int cell)
    {
        if (!CanUseHoePrimary(cell)) return false;
        if (GetCellState(cell) == GroundCellState.FieldSoil)
            return TrySetCellState(cell, GroundCellState.PlantingHole);
        if (soilCoverLayers[cell.x, cell.y] == byte.MaxValue) return false;
        soilCoverLayers[cell.x, cell.y]++;
        CellChanged?.Invoke(cell);
        return true;
    }

    public bool TryPlantPotato(Vector2Int cell)
    {
        if (!ContainsCell(cell) || GetCellState(cell) != GroundCellState.PlantingHole) return false;
        soilCoverLayers[cell.x, cell.y] = 1;
        return TrySetCellState(cell, GroundCellState.PotatoCovered);
    }

    public bool CanCoverPotato(Vector2Int cell)
    {
        var state = GetCellState(cell);
        return state >= GroundCellState.SproutSecond && state <= GroundCellState.ReadyToHarvest;
    }

    public bool TryCoverPotato(Vector2Int cell)
    {
        if (!CanCoverPotato(cell)) return false;
        int growthIndex = (int)GetCellState(cell) - (int)GroundCellState.SproutSecond;
        return TrySetCellState(cell, (GroundCellState)((int)GroundCellState.SoilCoveredFirst + growthIndex));
    }

    public bool IsSoilCoveredPotato(Vector2Int cell)
    {
        var state = GetCellState(cell);
        return state >= GroundCellState.SoilCoveredFirst && state <= GroundCellState.SoilCoveredFifth;
    }

    public bool CanUseSickle(Vector2Int cell)
    {
        var state = GetCellState(cell);
        return state >= GroundCellState.EarlyHarvest && state <= GroundCellState.LateHarvest
            || state >= GroundCellState.SoilCoveredThird && state <= GroundCellState.SoilCoveredFifth;
    }

    public bool TryUseSickle(Vector2Int cell)
    {
        if (!CanUseSickle(cell)) return false;
        bool covered = IsSoilCoveredPotato(cell);
        return TrySetCellState(cell, covered ? GroundCellState.CoveredCut : GroundCellState.UncoveredCut);
    }

    public bool CanUsePotatoFork(Vector2Int cell)
    {
        switch (GetCellState(cell))
        {
            case GroundCellState.UncoveredCut:
            case GroundCellState.UncoveredHighYield:
            case GroundCellState.UncoveredHighHidden:
            case GroundCellState.UncoveredMidEarly:
            case GroundCellState.UncoveredMidYield:
            case GroundCellState.UncoveredMidHidden:
            case GroundCellState.UncoveredLowEarly:
            case GroundCellState.UncoveredLowYield:
            case GroundCellState.CoveredCut:
            case GroundCellState.CoveredHighYield:
            case GroundCellState.CoveredHighHidden:
            case GroundCellState.CoveredMidEarly:
            case GroundCellState.CoveredMidYield:
            case GroundCellState.CoveredMidHidden:
            case GroundCellState.CoveredLowEarly:
            case GroundCellState.CoveredLowYield:
                return true;
            default:
                return false;
        }
    }

    public bool TryUsePotatoFork(Vector2Int cell)
    {
        if (!CanUsePotatoFork(cell)) return false;
        GroundCellState next;
        switch (GetCellState(cell))
        {
            case GroundCellState.UncoveredCut:
                next = UnityEngine.Random.value < 0.5f
                    ? GroundCellState.UncoveredHighYield : GroundCellState.UncoveredHighHidden;
                break;
            case GroundCellState.UncoveredHighYield:
                next = GroundCellState.UncoveredMidEarly;
                break;
            case GroundCellState.UncoveredHighHidden:
                next = UnityEngine.Random.value < 0.5f
                    ? GroundCellState.UncoveredMidYield : GroundCellState.UncoveredMidHidden;
                break;
            case GroundCellState.UncoveredMidYield:
                next = GroundCellState.UncoveredLowEarly;
                break;
            case GroundCellState.UncoveredMidHidden:
                next = GroundCellState.UncoveredLowYield;
                break;
            case GroundCellState.UncoveredMidEarly:
            case GroundCellState.UncoveredLowEarly:
            case GroundCellState.UncoveredLowYield:
                next = GroundCellState.PlantingHole;
                break;
            case GroundCellState.CoveredCut:
                next = UnityEngine.Random.value < 0.5f
                    ? GroundCellState.CoveredHighYield : GroundCellState.CoveredHighHidden;
                break;
            case GroundCellState.CoveredHighYield:
                next = GroundCellState.CoveredMidEarly;
                break;
            case GroundCellState.CoveredHighHidden:
                next = UnityEngine.Random.value < 0.5f
                    ? GroundCellState.CoveredMidYield : GroundCellState.CoveredMidHidden;
                break;
            case GroundCellState.CoveredMidYield:
                next = GroundCellState.CoveredLowEarly;
                break;
            case GroundCellState.CoveredMidHidden:
                next = GroundCellState.CoveredLowYield;
                break;
            case GroundCellState.CoveredMidEarly:
            case GroundCellState.CoveredLowEarly:
            case GroundCellState.CoveredLowYield:
                next = GroundCellState.PlantingHole;
                break;
            default:
                return false;
        }
        return TrySetCellState(cell, next);
    }

    public bool CanHarvestPotato(Vector2Int cell)
        => TryGetHarvestInfo(GetCellState(cell), out _, out _);

    public bool TryGetHarvestInfo(Vector2Int cell, out PotatoHarvestQuality quality,
        out string stateCode)
    {
        if (ContainsCell(cell)) return TryGetHarvestInfo(GetCellState(cell), out quality, out stateCode);
        quality = default;
        stateCode = string.Empty;
        return false;
    }

    public bool TryHarvestPotato(Vector2Int cell, out PotatoHarvestQuality quality, out string stateCode)
    {
        quality = default;
        stateCode = string.Empty;
        if (!TryGetHarvestInfo(cell, out quality, out stateCode))
            return false;

        if (!TrySetCellState(cell, GroundCellState.PlantingHole)) return false;
        string description = quality == PotatoHarvestQuality.Normal ? "정상적인 감자" : "손상된 감자";
        Debug.Log($"[Potato Harvest] {description}를 수확했습니다. (상태: {stateCode}, Cell={cell})", this);
        return true;
    }

    public bool TryRemovePotato(Vector2Int cell)
    {
        if (!HasPotato(cell)) return false;
        return TrySetCellState(cell, GroundCellState.FieldSoil);
    }

    public bool TryWater(Vector2Int cell)
    {
        if (!CanWater(cell)) return false;
        watered[cell.x, cell.y] = true;
        CellChanged?.Invoke(cell);
        return true;
    }

    /// <summary>
    /// Changes one cell to the requested state and notifies the ground renderer.
    /// Returns false when the cell is outside the grid or already has that state.
    /// </summary>
    public bool TrySetCellState(Vector2Int cell, GroundCellState state)
    {
        if (!ContainsCell(cell) || cells[cell.x, cell.y] == state) return false;
        GroundCellState previousState = cells[cell.x, cell.y];
        cells[cell.x, cell.y] = state;
        if (!HasPotatoState(state)) soilCoverLayers[cell.x, cell.y] = 0;
        if (!HasPotatoState(state)) watered[cell.x, cell.y] = false;
        CellChanged?.Invoke(cell);
        LogFarmingStateChange(cell, previousState, state);
        return true;
    }

    private static bool HasPotatoState(GroundCellState state)
        => state >= GroundCellState.PotatoCovered && state <= GroundCellState.CoveredLowYield;

    private static bool TryGetHarvestInfo(GroundCellState state, out PotatoHarvestQuality quality,
        out string stateCode)
    {
        switch (state)
        {
            case GroundCellState.UncoveredHighYield:
            case GroundCellState.CoveredHighYield:
                quality = PotatoHarvestQuality.Normal;
                stateCode = "HY";
                return true;
            case GroundCellState.UncoveredMidYield:
            case GroundCellState.CoveredMidYield:
                quality = PotatoHarvestQuality.Normal;
                stateCode = "MY";
                return true;
            case GroundCellState.UncoveredLowYield:
            case GroundCellState.CoveredLowYield:
                quality = PotatoHarvestQuality.Normal;
                stateCode = "LY";
                return true;
            case GroundCellState.UncoveredMidEarly:
            case GroundCellState.CoveredMidEarly:
                quality = PotatoHarvestQuality.Damaged;
                stateCode = "ME";
                return true;
            case GroundCellState.UncoveredLowEarly:
            case GroundCellState.CoveredLowEarly:
                quality = PotatoHarvestQuality.Damaged;
                stateCode = "LE";
                return true;
            default:
                quality = default;
                stateCode = string.Empty;
                return false;
        }
    }

    private static bool IsPreparedFarmState(GroundCellState state)
        => state == GroundCellState.FieldSoil || state == GroundCellState.PlantingHole || HasPotatoState(state);

    private void LogFarmingStateChange(Vector2Int cell, GroundCellState previousState, GroundCellState nextState)
    {
        if (previousState < GroundCellState.PotatoCovered && nextState < GroundCellState.PotatoCovered) return;

        Debug.Log(
            $"[Ground State] Ground='{name}', Cell={cell}, " +
            $"{FormatState(previousState)} -> {FormatState(nextState)}",
            this);
    }

    private static string FormatState(GroundCellState state)
    {
        if (state == GroundCellState.BasicSoil) return $"{state} (Ground N_001)";
        if (state == GroundCellState.FieldSoil) return state.ToString();
        return $"{state} (Visual {(int)state:00})";
    }

    public bool TryGetCell(Ray ray, float maximumDistance, out Vector2Int cell, out float distance)
    {
        cell = default;
        distance = 0f;
        var plane = new Plane(Vector3.up, Origin);
        if (!plane.Raycast(ray, out distance) || distance > maximumDistance) return false;
        cell = WorldToCell(ray.GetPoint(distance));
        return ContainsCell(cell);
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 size = new Vector3(Width * CellSize, 0f, Depth * CellSize);
        Gizmos.DrawWireCube(Origin + size * 0.5f, size);
        if (Width + Depth > 256) return;
        for (int x = 0; x <= Width; x++)
            Gizmos.DrawLine(Origin + Vector3.right * (x * CellSize), Origin + new Vector3(x * CellSize, 0f, size.z));
        for (int z = 0; z <= Depth; z++)
            Gizmos.DrawLine(Origin + Vector3.forward * (z * CellSize), Origin + new Vector3(size.x, 0f, z * CellSize));
    }
}
