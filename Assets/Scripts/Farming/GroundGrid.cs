using System;
using UnityEngine;

public enum GroundCellState : byte
{
    BasicSoil = 0,
    FieldSoil = 1,
    PlantingHole = 2,        // Tile 016
    PotatoCovered = 3,       // Tile 017
    SproutFirst = 4,         // Tile 018
    SproutSecond = 5,        // Tile 019
    SproutThird = 6,         // Tile 020
    EarlyHarvest = 7,        // Tile 021
    ReadyToHarvest = 8,      // Tile 022
    LateHarvest = 9,         // Tile 023
    BoarDugField = 10        // Tile 024
}

public sealed class GroundGrid : MonoBehaviour
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
        return state >= GroundCellState.PotatoCovered && state <= GroundCellState.LateHarvest;
    }
    public bool CanPlant(Vector2Int cell) => GetCellState(cell) == GroundCellState.PlantingHole;
    public bool IsWatered(Vector2Int cell) => ContainsCell(cell) && watered[cell.x, cell.y];
    public bool CanWater(Vector2Int cell) => IsDug(cell) && !IsWatered(cell);
    public bool IsDug(Vector2Int cell) => ContainsCell(cell) && cells[cell.x, cell.y] != GroundCellState.BasicSoil;
    public bool CanDig(Vector2Int cell) => ContainsCell(cell) && cells[cell.x, cell.y] == GroundCellState.BasicSoil;
    public bool CanFill(Vector2Int cell) => ContainsCell(cell) && cells[cell.x, cell.y] != GroundCellState.BasicSoil;
    public bool TryDig(Vector2Int cell) => TrySetCellState(cell, GroundCellState.FieldSoil);
    public bool TryFill(Vector2Int cell) => TrySetCellState(cell, GroundCellState.BasicSoil);

    public bool CanUseHoeSecondary(Vector2Int cell)
    {
        var state = GetCellState(cell);
        return state == GroundCellState.BasicSoil || state == GroundCellState.FieldSoil;
    }
    public bool CanUseHoePrimary(Vector2Int cell)
    {
        var state = GetCellState(cell);
        return state == GroundCellState.FieldSoil || HasPotato(cell);
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
        if (state == GroundCellState.BasicSoil) watered[cell.x, cell.y] = false;
        CellChanged?.Invoke(cell);
        LogFarmingStateChange(cell, previousState, state);
        return true;
    }

    private static bool HasPotatoState(GroundCellState state)
        => state >= GroundCellState.PotatoCovered && state <= GroundCellState.LateHarvest;

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
        int tileNumber = state >= GroundCellState.PlantingHole
            ? 16 + (int)state - (int)GroundCellState.PlantingHole
            : state == GroundCellState.BasicSoil ? 1 : 0;
        return tileNumber > 0 ? $"{state} (Tile {tileNumber:000})" : state.ToString();
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
