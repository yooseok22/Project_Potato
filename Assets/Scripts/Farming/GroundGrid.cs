using UnityEngine;

[RequireComponent(typeof(MeshCollider))]
public sealed class GroundGrid : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float cellSize = 1f;
    private MeshCollider surface;
    public float CellSize => Mathf.Max(0.01f, cellSize);
    public MeshCollider Surface => surface ? surface : surface = GetComponent<MeshCollider>();
    public Vector3 Origin => Surface.bounds.min;
    
    public Vector2Int WorldToCell(Vector3 point)
    {
        Vector3 relative = point - Origin;
        return new Vector2Int(Mathf.FloorToInt(relative.x / CellSize), Mathf.FloorToInt(relative.z / CellSize));
    }

    public Vector3 CellCenter(Vector2Int cell)
    {
        Vector3 origin = Origin;
        return new Vector3(origin.x + (cell.x + 0.5f) * CellSize, Surface.bounds.max.y,
            origin.z + (cell.y + 0.5f) * CellSize);
    }

    public bool ContainsCell(Vector2Int cell)
    {
        Vector3 size = Surface.bounds.size;
        return cell.x >= 0 && cell.y >= 0 &&
            (cell.x + 1) * CellSize <= size.x + 0.0001f &&
            (cell.y + 1) * CellSize <= size.z + 0.0001f;
    }
}
