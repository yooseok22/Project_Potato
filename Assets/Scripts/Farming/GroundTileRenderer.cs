using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(GroundGrid))]
public sealed class GroundTileRenderer : MonoBehaviour
{
    [SerializeField] private GameObject solidPrefab;
    [SerializeField] private GameObject cornerPrefab;
    [SerializeField] private GameObject edgePrefab;
    [SerializeField] private GameObject dugPrefab;
    [SerializeField] private GameObject innerCornerPrefab;
    [SerializeField, Min(0.01f)] private float prefabCellSize = 2f;
    [SerializeField, Range(1, 64)] private int chunkSize = 16;

    private sealed class Chunk
    {
        public GameObject Object;
        public Mesh Mesh;
        public MeshFilter Filter;
        public MeshCollider Collider;
    }
    private GroundGrid grid;
    private Chunk[,] chunks;
    private TileVisualResolver.Visual[,] visuals;
    private TileMeshPatch[,,] patches;
    private readonly List<Material> materials = new List<Material>();
    private Transform container;
    private bool originalSurfaceEnabled;
    private bool started;
    public int LastChangedCount { get; private set; }
    public int LastRebuiltChunkCount { get; private set; }
    public int ChunkCount => chunks == null ? 0 : chunks.Length;
    public bool IsReady => chunks != null;

    private void OnEnable()
    {
        grid = GetComponent<GroundGrid>();
        grid.CellChanged += RefreshAround;
        grid.AreaChanged += RebuildArea;
        if (started) Build();
    }
    private void Start() { started = true; Build(); }
    private void OnDisable()
    {
        if (grid) { grid.CellChanged -= RefreshAround; grid.AreaChanged -= RebuildArea; }
        Clear();
    }
    private void RebuildArea() { if (started) Build(); }

    public void Build()
    {
        Clear();
        grid = GetComponent<GroundGrid>();
        var prefabs = new[] { solidPrefab, cornerPrefab, edgePrefab, dugPrefab, innerCornerPrefab };
        foreach (var prefab in prefabs)
            if (!prefab) { Debug.LogError("Assign all five tile mesh prefabs, including the inner corner.", this); return; }
        chunkSize = Mathf.Clamp(chunkSize, 1, 64);
        materials.Clear();
        try
        {
            // Clip source triangles once, retaining their UVs, normals and material assignments.
            patches = new TileMeshPatch[5, 4, 4];
            for (int kind = 0; kind < 5; kind++)
                for (int turns = 0; turns < 4; turns++)
                    for (int quadrant = 0; quadrant < 4; quadrant++)
                        patches[kind, turns, quadrant] = TileMeshPatch.Create(prefabs[kind], turns, quadrant, materials);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("Unable to read tile source meshes. Enable Read/Write on their FBX importers. " + exception.Message, this);
            patches = null;
            return;
        }
        visuals = new TileVisualResolver.Visual[grid.Width, grid.Depth];
        for (int x = 0; x < grid.Width; x++) for (int z = 0; z < grid.Depth; z++)
            visuals[x,z] = TileVisualResolver.Resolve(grid, new Vector2Int(x,z));
        // An unscaled world-space container keeps the explicit grid independent of the old floor transform.
        container = new GameObject("GroundChunks").transform;
        container.SetParent(transform, false);
        container.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        container.localScale = new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z);
        int columns = (grid.Width + chunkSize - 1) / chunkSize;
        int rows = (grid.Depth + chunkSize - 1) / chunkSize;
        chunks = new Chunk[columns, rows];
        for (int x = 0; x < columns; x++) for (int z = 0; z < rows; z++)
        {
            var go = new GameObject($"Chunk_{x}_{z}", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.layer = gameObject.layer;
            go.transform.SetParent(container, false);
            go.transform.position = grid.Origin + new Vector3(x * chunkSize * grid.CellSize, 0f, z * chunkSize * grid.CellSize);
            go.GetComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            var mesh = new Mesh { name = $"GroundChunk_{x}_{z}", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            var chunk = new Chunk
            {
                Object = go, Mesh = mesh, Filter = go.GetComponent<MeshFilter>(),
                Collider = go.GetComponent<MeshCollider>()
            };
            chunk.Collider.convex = false;
            chunk.Collider.isTrigger = false;
            chunk.Filter.sharedMesh = mesh;
            chunks[x,z] = chunk;
            RebuildChunk(x,z);
        }
        var surface = GetComponent<MeshRenderer>();
        if (surface) { originalSurfaceEnabled = surface.enabled; surface.enabled = false; }
        LastChangedCount = LastRebuiltChunkCount = 0;
    }

    private void RefreshAround(Vector2Int cell)
    {
        if (!IsReady) return;
        LastChangedCount = LastRebuiltChunkCount = 0;
        var dirty = new HashSet<Vector2Int>();
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
        {
            var target = cell + new Vector2Int(x,z);
            if (!grid.ContainsCell(target)) continue;
            var visual = TileVisualResolver.Resolve(grid, target);
            if (visuals[target.x,target.y].SameAs(visual)) continue;
            visuals[target.x,target.y] = visual;
            LastChangedCount++;
            dirty.Add(new Vector2Int(target.x / chunkSize, target.y / chunkSize));
        }
        foreach (var coordinate in dirty) { RebuildChunk(coordinate.x, coordinate.y); LastRebuiltChunkCount++; }
    }

    private void RebuildChunk(int column, int row)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>[materials.Count];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = new List<int>();
        int startX = column * chunkSize, startZ = row * chunkSize;
        float scale = grid.CellSize / prefabCellSize;
        for (int x = startX; x < Mathf.Min(startX + chunkSize, grid.Width); x++)
            for (int z = startZ; z < Mathf.Min(startZ + chunkSize, grid.Depth); z++)
            {
                var offset = new Vector3((x - startX + 0.5f) * grid.CellSize, 0f, (z - startZ + 0.5f) * grid.CellSize);
                for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    var piece = visuals[x,z].GetPiece(quadrant);
                    patches[piece.Kind, piece.QuarterTurns, quadrant].Append(vertices, normals, uv, triangles, offset, scale);
                }
            }
        var chunk = chunks[column,row];
        // Detach the previous cooked shape before editing its source mesh.
        chunk.Collider.sharedMesh = null;
        var mesh = chunk.Mesh;
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = materials.Count;
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i, false);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        // Recook only this changed chunk, using exactly the rendered surface.
        chunk.Collider.sharedMesh = mesh;
    }

    private void Clear()
    {
        if (chunks != null)
        {
            foreach (var chunk in chunks)
            {
                if (chunk == null) continue;
                if (chunk.Collider) { chunk.Collider.enabled = false; chunk.Collider.sharedMesh = null; }
                if (chunk.Mesh) Destroy(chunk.Mesh);
            }
            var surface = GetComponent<MeshRenderer>();
            if (surface) surface.enabled = originalSurfaceEnabled;
        }
        if (container) { container.gameObject.SetActive(false); Destroy(container.gameObject); }
        container = null;
        chunks = null;
        visuals = null;
        patches = null;
    }
}
