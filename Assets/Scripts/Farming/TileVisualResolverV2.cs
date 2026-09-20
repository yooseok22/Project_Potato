using UnityEngine;

// V2 selects one complete authored tile per grid cell. The four cardinal
// neighbours choose the connection shape; diagonals are only needed for the
// fully surrounded 008 tile.
public static class TileVisualResolverV2
{
    public struct Visual
    {
        public int Kind; // 0..7 maps directly to v2-001..v2-008.
        public int QuarterTurns;

        public int Signature => Kind | (QuarterTurns << 3);
        public bool SameAs(Visual other) => Signature == other.Signature;
    }

    // Bit order follows clockwise directions: north, east, south, west.
    private const int North = 1 << 0;
    private const int East = 1 << 1;
    private const int South = 1 << 2;
    private const int West = 1 << 3;
    private const int AllCardinals = North | East | South | West;

    private static readonly Vector2Int[] CardinalOffsets =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    private static readonly Vector2Int[] DiagonalOffsets =
    {
        new Vector2Int(1, 1), new Vector2Int(1, -1),
        new Vector2Int(-1, -1), new Vector2Int(-1, 1)
    };

    public static Visual Resolve(GroundGrid grid, Vector2Int cell)
    {
        if (!grid.IsDug(cell)) return new Visual { Kind = 0 };

        int mask = 0;
        for (int direction = 0; direction < CardinalOffsets.Length; direction++)
            if (grid.IsDug(cell + CardinalOffsets[direction])) mask |= 1 << direction;

        if (mask == AllCardinals && AllDiagonalsDug(grid, cell))
            return new Visual { Kind = 7 }; // 008, entirely surrounded.

        int count = CountBits(mask);
        switch (count)
        {
            case 0:
                return new Visual { Kind = 1 }; // 002, isolated hole.
            case 1:
                return Rotated(2, West, mask); // 003, one connection.
            case 2:
                if (mask == (North | South) || mask == (East | West))
                    return Rotated(3, East | West, mask); // 004, straight.
                return Rotated(5, South | West, mask); // 006, corner.
            case 3:
                return Rotated(6, North | East | West, mask); // 007, T junction.
            default:
                return new Visual { Kind = 4 }; // 005, four-way junction.
        }
    }

    private static bool AllDiagonalsDug(GroundGrid grid, Vector2Int cell)
    {
        foreach (var offset in DiagonalOffsets)
            if (!grid.IsDug(cell + offset)) return false;
        return true;
    }

    private static Visual Rotated(int kind, int baseMask, int targetMask)
    {
        for (int turns = 0; turns < 4; turns++)
            if (RotateMask(baseMask, turns) == targetMask)
                return new Visual { Kind = kind, QuarterTurns = turns };

        Debug.LogError($"No V2 tile rotation maps mask {baseMask} to {targetMask}.");
        return new Visual { Kind = kind };
    }

    private static int RotateMask(int mask, int turns)
    {
        turns &= 3;
        return ((mask << turns) | (mask >> (4 - turns))) & AllCardinals;
    }

    private static int CountBits(int value)
    {
        int count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }
        return count;
    }
}
