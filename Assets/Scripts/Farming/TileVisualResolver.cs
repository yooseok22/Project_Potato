using UnityEngine;

// Each quarter selects an artist-authored surface from its two face neighbours and diagonal.
// This covers all 256 neighbour masks without blocking a change of digging direction.
public static class TileVisualResolver
{
    public struct Piece
    {
        public int Kind; // 0 solid, 1 outer corner, 2 edge, 3 dug, 4 inner corner
        public int QuarterTurns;
    }
    public struct Visual
    {
        public int Signature;
        public bool SameAs(Visual other) => Signature == other.Signature;
        public Piece GetPiece(int quadrant)
        {
            int value = (Signature >> (quadrant * 5)) & 31;
            return new Piece { Kind = value & 7, QuarterTurns = value >> 3 };
        }
    }
    // Quadrants: SW, NW, NE, SE. Unity +90 Y rotates west toward north.
    private static readonly Vector2Int[] Faces = { Vector2Int.left, Vector2Int.up, Vector2Int.right, Vector2Int.down };
    private static readonly Vector2Int[] Corners = { new Vector2Int(-1,-1), new Vector2Int(-1,1), new Vector2Int(1,1), new Vector2Int(1,-1) };
    private static readonly int[] FirstFace = { 0, 0, 2, 2 };
    private static readonly int[] SecondFace = { 3, 1, 1, 3 };

    public static Visual Resolve(GroundGrid grid, Vector2Int cell)
    {
        var visual = new Visual();
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            int kind = 0, turns = 0;
            bool first = grid.IsDug(cell + Faces[FirstFace[quadrant]]);
            bool second = grid.IsDug(cell + Faces[SecondFace[quadrant]]);
            if (grid.IsDug(cell)) kind = 3;
            else if (first && second)
            {
                kind = 4;
                // Cube.571's raw mesh slopes toward east and south. Ignore its saved root pose.
                turns = (quadrant + 1) % 4;
            }
            else if (first || second)
            {
                kind = 2;
                turns = first ? FirstFace[quadrant] : SecondFace[quadrant];
            }
            else if (grid.IsDug(cell + Corners[quadrant]))
            {
                kind = 1;
                turns = quadrant;
            }
            visual.Signature |= (kind | (turns << 3)) << (quadrant * 5);
        }
        return visual;
    }
}
