using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Connected work tiles. Each added tile must share an edge with an existing tile.</summary>
[Serializable]
public sealed class FarmRobotRoute
{
    [SerializeField] private List<Vector2Int> cells = new();
    [SerializeField] private List<bool> work = new();
    public IReadOnlyList<Vector2Int> Cells => cells;
    public int Count => cells.Count;
    public bool WorksAt(int index) => index >= 0 && index < work.Count && work[index];
    public bool WorksAt(Vector2Int cell) => WorksAt(cells.IndexOf(cell));

    public bool TryAdd(Vector2Int cell, Func<Vector2Int, bool> allowed)
    {
        if (!allowed(cell)) return false;
        if (cells.Contains(cell)) return true;
        if (cells.Count > 0 && !cells.Exists(existing => (existing - cell).sqrMagnitude == 1)) return false;
        cells.Add(cell); work.Add(true); return true;
    }

    public void RemoveLast()
    {
        if (cells.Count == 0) return;
        cells.RemoveAt(cells.Count - 1); work.RemoveAt(work.Count - 1);
    }

    /// <summary>Visit each branch in placement order and retrace its edges, ending at the first tile.</summary>
    public List<FarmRobotVisit> BuildRoundTrip()
    {
        var result = new List<FarmRobotVisit>();
        if (cells.Count == 0) return result;
        var indexes = new Dictionary<Vector2Int, int>();
        for (int index = 0; index < cells.Count; index++) indexes[cells[index]] = index;
        var seen = new HashSet<int> { 0 }; var stack = new Stack<int>(); stack.Push(0);
        var offsets = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        result.Add(new FarmRobotVisit(cells[0], false));
        while (stack.Count > 0)
        {
            int current = stack.Peek(), next = -1;
            foreach (var offset in offsets)
                if (indexes.TryGetValue(cells[current] + offset, out int candidate) && !seen.Contains(candidate) &&
                    (next < 0 || candidate < next)) next = candidate;
            if (next >= 0)
            {
                seen.Add(next); stack.Push(next); result.Add(new FarmRobotVisit(cells[next], false));
            }
            else
            {
                stack.Pop();
                if (stack.Count > 0) result.Add(new FarmRobotVisit(cells[stack.Peek()], true));
            }
        }
        return result;
    }

    // Convenience builder for rectangular test fixtures; interactive editing uses TryAdd per tile.
    public bool TrySetEnd(Vector2Int start, Vector2Int end, Func<Vector2Int, bool> allowed)
    {
        Vector2Int delta = end - start;
        if (delta.x != 0 && delta.y != 0) return false;
        int count = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
        Vector2Int step = new(Math.Sign(delta.x), Math.Sign(delta.y));
        var replacement = new List<Vector2Int>();
        var flags = new List<bool>();
        for (int index = 0; index <= count; index++)
        {
            var cell = start + step * index;
            if (!allowed(cell)) return false;
            int previous = cells.IndexOf(cell);
            replacement.Add(cell); flags.Add(previous < 0 || work[previous]);
        }
        cells = replacement; work = flags; return true;
    }
    public bool Toggle(Vector2Int cell)
    {
        int index = cells.IndexOf(cell); if (index < 0) return false;
        work[index] = !work[index]; return true;
    }
    public FarmRobotRoute Copy()
    {
        var copy = new FarmRobotRoute(); copy.cells.AddRange(cells); copy.work.AddRange(work); return copy;
    }
}

public readonly struct FarmRobotVisit
{
    public readonly Vector2Int Cell;
    public readonly bool Returning;
    public FarmRobotVisit(Vector2Int cell, bool returning) { Cell = cell; Returning = returning; }
}

[Serializable]
public struct FarmDroneArea
{
    public Vector2Int minimum;
    public Vector2Int maximum;
    public Vector2Int Center => new((minimum.x + maximum.x) / 2, (minimum.y + maximum.y) / 2);
    public bool Contains(Vector2Int cell) => cell.x >= minimum.x && cell.y >= minimum.y && cell.x <= maximum.x && cell.y <= maximum.y;
}
