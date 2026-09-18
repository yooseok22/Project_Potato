using UnityEngine;

public static class GroundDigAction
{
    public static bool CanDig(GroundGrid grid, Vector2Int cell) => grid && grid.CanDig(cell);
}
