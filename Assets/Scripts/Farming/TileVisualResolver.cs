using UnityEngine;

// Selects one complete authored tile per grid cell from the final N/G/GB/GN set.
public static class TileVisualResolver
{
    public struct Visual
    {
        public int Kind; // 0..43 maps directly to GroundTileRenderer's prefab array.
        public int QuarterTurns;
        public bool MirrorX;

        public int Signature => Kind | (QuarterTurns << 6) | (MirrorX ? 1 << 8 : 0);
        public bool SameAs(Visual other) => Signature == other.Signature;
    }

    // Bit order follows clockwise directions: north, east, south, west.
    private const int North = 1 << 0;
    private const int East = 1 << 1;
    private const int South = 1 << 2;
    private const int West = 1 << 3;
    private const int AllCardinals = North | East | South | West;

    // Diagonal bit order also follows clockwise directions.
    private const int NorthEast = 1 << 0;
    private const int SouthEast = 1 << 1;
    private const int SouthWest = 1 << 2;
    private const int NorthWest = 1 << 3;
    private const int AllDiagonals = NorthEast | SouthEast | SouthWest | NorthWest;

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
        var state = grid.GetCellState(cell);
        if (state == GroundCellState.BasicSoil) return new Visual { Kind = 0 };
        if (state == GroundCellState.HarvestClearedField) return new Visual { Kind = 14 }; // Fixed N_015.
        if (state != GroundCellState.FieldSoil)
            return new Visual { Kind = (int)state };

        int cardinalMask = 0;
        for (int direction = 0; direction < CardinalOffsets.Length; direction++)
            if (grid.IsDug(cell + CardinalOffsets[direction])) cardinalMask |= 1 << direction;

        int diagonalMask = 0;
        for (int direction = 0; direction < DiagonalOffsets.Length; direction++)
            if (grid.IsDug(cell + DiagonalOffsets[direction])) diagonalMask |= 1 << direction;

        int count = CountBits(cardinalMask);
        switch (count)
        {
            case 0:
                return new Visual { Kind = 1 }; // 002, isolated hole.
            case 1:
                return Transformed(2, North, 0, cardinalMask, 0); // 003, one connection.
            case 2:
                if (cardinalMask == (North | South) || cardinalMask == (East | West))
                    return Transformed(3, North | South, 0, cardinalMask, 0); // 004, straight.
                return ResolveCorner(cardinalMask, diagonalMask);
            case 3:
                return ResolveThreeWay(cardinalMask, diagonalMask);
            default:
                return ResolveFourWay(diagonalMask);
        }
    }

    private static Visual ResolveCorner(int cardinalMask, int diagonalMask)
    {
        int sharedDiagonal = SharedDiagonal(cardinalMask);
        if ((diagonalMask & sharedDiagonal) != 0)
            return Transformed(4, North | West, NorthWest, cardinalMask, sharedDiagonal); // 005.

        return Transformed(6, East | South, 0, cardinalMask, 0); // 007, raised inside corner.
    }

    private static Visual ResolveThreeWay(int cardinalMask, int diagonalMask)
    {
        int relevant = RelevantThreeWayDiagonals(cardinalMask);
        int dugRelevant = diagonalMask & relevant;
        int missingCount = 2 - CountBits(dugRelevant);

        if (missingCount == 0)
            return Transformed(5, North | East | South, NorthEast | SouthEast,
                cardinalMask, relevant); // 006.
        if (missingCount == 1)
            return Transformed(7, North | East | South, NorthEast,
                cardinalMask, dugRelevant); // 008.

        return Transformed(8, North | East | South, 0,
            cardinalMask, 0); // 009, both exposed diagonals remain undug.
    }

    private static Visual ResolveFourWay(int diagonalMask)
    {
        int missingMask = AllDiagonals & ~diagonalMask;
        switch (CountBits(missingMask))
        {
            case 0:
                return new Visual { Kind = 14 }; // 015, all eight neighbours dug.
            case 1:
                return Transformed(9, AllCardinals, AllDiagonals & ~SouthEast,
                    AllCardinals, diagonalMask); // 010.
            case 2:
                // 011 covers adjacent raised corners; 012 covers the formerly
                // missing opposite-corner case in the expanded 001-015 set.
                if (AreOppositeDiagonals(diagonalMask))
                    return Transformed(11, AllCardinals, NorthWest | SouthEast,
                        AllCardinals, diagonalMask); // 012.
                return Transformed(10, AllCardinals, NorthEast | NorthWest,
                    AllCardinals, diagonalMask); // 011.
            case 3:
                return Transformed(12, AllCardinals, SouthEast,
                    AllCardinals, diagonalMask); // 013.
            default:
                return new Visual { Kind = 13 }; // 014, all diagonals remain undug.
        }
    }

    private static bool AreOppositeDiagonals(int diagonalMask)
    {
        return diagonalMask == (NorthEast | SouthWest) ||
               diagonalMask == (SouthEast | NorthWest);
    }

    private static int SharedDiagonal(int cardinalMask)
    {
        if ((cardinalMask & (North | East)) == (North | East)) return NorthEast;
        if ((cardinalMask & (East | South)) == (East | South)) return SouthEast;
        if ((cardinalMask & (South | West)) == (South | West)) return SouthWest;
        return NorthWest;
    }

    private static int RelevantThreeWayDiagonals(int cardinalMask)
    {
        if ((cardinalMask & West) == 0) return NorthEast | SouthEast;
        if ((cardinalMask & North) == 0) return SouthEast | SouthWest;
        if ((cardinalMask & East) == 0) return SouthWest | NorthWest;
        return NorthWest | NorthEast;
    }

    private static Visual Transformed(int kind, int baseCardinals, int baseDiagonals,
        int targetCardinals, int targetDiagonals)
    {
        for (int mirror = 0; mirror < 2; mirror++)
            for (int turns = 0; turns < 4; turns++)
                if (TransformCardinalMask(baseCardinals, turns, mirror != 0) == targetCardinals &&
                    TransformDiagonalMask(baseDiagonals, turns, mirror != 0) == targetDiagonals)
                    return new Visual { Kind = kind, QuarterTurns = turns, MirrorX = mirror != 0 };

        Debug.LogError($"No tile transform maps cardinal {baseCardinals}/{targetCardinals} " +
            $"and diagonal {baseDiagonals}/{targetDiagonals}.");
        return new Visual { Kind = kind };
    }

    private static int TransformCardinalMask(int mask, int turns, bool mirrorX)
    {
        if (mirrorX)
        {
            int mirrored = mask & (North | South);
            if ((mask & East) != 0) mirrored |= West;
            if ((mask & West) != 0) mirrored |= East;
            mask = mirrored;
        }
        turns &= 3;
        return ((mask << turns) | (mask >> (4 - turns))) & AllCardinals;
    }

    private static int TransformDiagonalMask(int mask, int turns, bool mirrorX)
    {
        if (mirrorX)
        {
            int mirrored = 0;
            if ((mask & NorthEast) != 0) mirrored |= NorthWest;
            if ((mask & SouthEast) != 0) mirrored |= SouthWest;
            if ((mask & SouthWest) != 0) mirrored |= SouthEast;
            if ((mask & NorthWest) != 0) mirrored |= NorthEast;
            mask = mirrored;
        }
        turns &= 3;
        return ((mask << turns) | (mask >> (4 - turns))) & AllDiagonals;
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
