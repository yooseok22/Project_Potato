using System;
using UnityEngine;

public sealed class GroundGrid : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float cellSize = 2f;
    [SerializeField, Min(1)] private int width = 50;
    [SerializeField, Min(1)] private int depth = 50;
    [Tooltip("World-space minimum corner of the grid; Y is the selection surface height.")]
    [SerializeField] private Vector3 origin = new Vector3(-50f, 0f, -50f);
    private bool[,] dug;
    private bool[,] blocked;
    public event Action<Vector2Int> CellChanged;
    public event Action AreaChanged;
    public float CellSize => Mathf.Max(0.01f, cellSize);
    public int Width => Mathf.Max(1, width);
    public int Depth => Mathf.Max(1, depth);
    public Vector3 Origin => origin;
    // Compatibility only: collision geometry never determines the logical grid.
    public MeshCollider Surface => GetComponent<MeshCollider>();

    private void Awake() => EnsureCells();
    private void EnsureCells()
    {
        if (dug != null && dug.GetLength(0) == Width && dug.GetLength(1) == Depth) return;
        dug = new bool[Width, Depth];
        blocked = new bool[Width, Depth];
    }

    public void ConfigureArea(Vector3 minimumCorner, int columns, int rows, float size)
    {
        origin = minimumCorner;
        width = Mathf.Max(1, columns);
        depth = Mathf.Max(1, rows);
        cellSize = Mathf.Max(0.01f, size);
        dug = blocked = null;
        EnsureCells();
        AreaChanged?.Invoke();
    }

    public Vector2Int WorldToCell(Vector3 point) => new Vector2Int(
        Mathf.FloorToInt((point.x - Origin.x) / CellSize),
        Mathf.FloorToInt((point.z - Origin.z) / CellSize));

    public Vector3 CellCenter(Vector2Int cell) => Origin + new Vector3(
        (cell.x + 0.5f) * CellSize, 0f, (cell.y + 0.5f) * CellSize);

    public bool ContainsCell(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Depth;
    public bool IsDug(Vector2Int cell)
    {
        EnsureCells();
        return ContainsCell(cell) && dug[cell.x, cell.y];
    }
    public bool CanDig(Vector2Int cell)
    {
        EnsureCells();
        return ContainsCell(cell) && !dug[cell.x, cell.y] && !blocked[cell.x, cell.y];
    }
    public bool CanFill(Vector2Int cell)
    {
        EnsureCells();
        return ContainsCell(cell) && dug[cell.x, cell.y] && !blocked[cell.x, cell.y];
    }
    public void SetBlocked(Vector2Int cell, bool value)
    {
        EnsureCells();
        if (ContainsCell(cell)) blocked[cell.x, cell.y] = value;
    }
    public bool TryDig(Vector2Int cell)
    {
        if (!GroundDigAction.CanDig(this, cell)) return false;
        dug[cell.x, cell.y] = true;
        CellChanged?.Invoke(cell);
        return true;
    }
    public bool TryFill(Vector2Int cell)
    {
        if (!CanFill(cell)) return false;
        dug[cell.x, cell.y] = false;
        CellChanged?.Invoke(cell);
        return true;
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
