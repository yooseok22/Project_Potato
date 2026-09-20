using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(GroundGrid))]
public sealed class GroundTileRendererV2 : MonoBehaviour
{
    [Header("V2 complete-cell tiles (001-008)")]
    [SerializeField] private GameObject[] tilePrefabs = new GameObject[8];
    [SerializeField, Min(0.01f)] private float prefabCellSize = 2f;
    [SerializeField, Range(1, 64)] private int chunkSize = 16;

    private sealed class Chunk
    {
        public Mesh Mesh;
        public MeshFilter Filter;
        public MeshCollider Collider;
    }

    private GroundGrid grid;
    private Chunk[,] chunks;
    private TileVisualResolverV2.Visual[,] visuals;
    private TileMeshSource[] sources;
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
        if (Application.isPlaying)
        {
            started = true;
            Build();
        }
    }

    private void Start()
    {
        if (started) return;
        started = true;
        Build();
    }

    private void OnDisable()
    {
        if (grid)
            grid.CellChanged -= RefreshAround;
        Clear();
    }

    public void Build()
    {
        Clear();
        grid = GetComponent<GroundGrid>();
        if (tilePrefabs == null || tilePrefabs.Length != 8)
        {
            Debug.LogError("Assign exactly eight V2 tile prefabs (001-008).", this);
            return;
        }
        foreach (var prefab in tilePrefabs)
        {
            if (prefab) continue;
            Debug.LogError("Assign all eight V2 tile prefabs (001-008).", this);
            return;
        }

        chunkSize = Mathf.Clamp(chunkSize, 1, 64);
        materials.Clear();
        try
        {
            sources = new TileMeshSource[8];
            for (int index = 0; index < sources.Length; index++)
                sources[index] = TileMeshSource.Create(tilePrefabs[index], materials);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("Unable to read a V2 tile mesh. Enable Read/Write on its FBX importer. " + exception.Message, this);
            sources = null;
            return;
        }

        visuals = new TileVisualResolverV2.Visual[grid.Width, grid.Depth];
        for (int x = 0; x < grid.Width; x++)
            for (int z = 0; z < grid.Depth; z++)
                visuals[x, z] = TileVisualResolverV2.Resolve(grid, new Vector2Int(x, z));

        container = new GameObject("GroundChunks_V2").transform;
        container.SetParent(transform, false);
        container.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        container.localScale = new Vector3(
            1f / transform.lossyScale.x,
            1f / transform.lossyScale.y,
            1f / transform.lossyScale.z);

        int columns = (grid.Width + chunkSize - 1) / chunkSize;
        int rows = (grid.Depth + chunkSize - 1) / chunkSize;
        chunks = new Chunk[columns, rows];
        for (int x = 0; x < columns; x++)
            for (int z = 0; z < rows; z++)
            {
                var go = new GameObject($"ChunkV2_{x}_{z}", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                go.layer = gameObject.layer;
                go.transform.SetParent(container, false);
                go.transform.position = grid.Origin + new Vector3(x * chunkSize * grid.CellSize, 0f, z * chunkSize * grid.CellSize);
                go.GetComponent<MeshRenderer>().sharedMaterials = materials.ToArray();

                var mesh = new Mesh { name = $"GroundChunkV2_{x}_{z}", indexFormat = IndexFormat.UInt32 };
                mesh.MarkDynamic();
                var chunk = new Chunk
                {
                    Mesh = mesh,
                    Filter = go.GetComponent<MeshFilter>(),
                    Collider = go.GetComponent<MeshCollider>()
                };
                chunk.Collider.convex = false;
                chunk.Collider.isTrigger = false;
                chunk.Filter.sharedMesh = mesh;
                chunks[x, z] = chunk;
                RebuildChunk(x, z);
            }

        var surface = GetComponent<MeshRenderer>();
        if (surface)
        {
            originalSurfaceEnabled = surface.enabled;
            surface.enabled = false;
        }
        LastChangedCount = LastRebuiltChunkCount = 0;
    }

    public TileVisualResolverV2.Visual GetVisual(Vector2Int cell)
    {
        if (!grid || !grid.ContainsCell(cell)) return default;
        return TileVisualResolverV2.Resolve(grid, cell);
    }

    private void RefreshAround(Vector2Int cell)
    {
        if (!IsReady) return;
        LastChangedCount = LastRebuiltChunkCount = 0;
        var dirty = new HashSet<Vector2Int>();
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                var target = cell + new Vector2Int(x, z);
                if (!grid.ContainsCell(target)) continue;
                var visual = TileVisualResolverV2.Resolve(grid, target);
                if (visuals[target.x, target.y].SameAs(visual)) continue;
                visuals[target.x, target.y] = visual;
                LastChangedCount++;
                dirty.Add(new Vector2Int(target.x / chunkSize, target.y / chunkSize));
            }
        foreach (var coordinate in dirty)
        {
            RebuildChunk(coordinate.x, coordinate.y);
            LastRebuiltChunkCount++;
        }
    }

    private void RebuildChunk(int column, int row)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>[materials.Count];
        for (int index = 0; index < triangles.Length; index++) triangles[index] = new List<int>();

        int startX = column * chunkSize;
        int startZ = row * chunkSize;
        float scale = grid.CellSize / prefabCellSize;
        for (int x = startX; x < Mathf.Min(startX + chunkSize, grid.Width); x++)
            for (int z = startZ; z < Mathf.Min(startZ + chunkSize, grid.Depth); z++)
            {
                var visual = visuals[x, z];
                var offset = new Vector3(
                    (x - startX + 0.5f) * grid.CellSize,
                    0f,
                    (z - startZ + 0.5f) * grid.CellSize);
                sources[visual.Kind].Append(vertices, normals, uv, triangles, offset, scale, visual.QuarterTurns);
            }

        var chunk = chunks[column, row];
        chunk.Collider.sharedMesh = null;
        var mesh = chunk.Mesh;
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = materials.Count;
        for (int index = 0; index < triangles.Length; index++) mesh.SetTriangles(triangles[index], index, false);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        chunk.Collider.sharedMesh = mesh;
    }

    private void Clear()
    {
        if (chunks != null)
        {
            foreach (var chunk in chunks)
            {
                if (chunk == null) continue;
                if (chunk.Collider)
                {
                    chunk.Collider.enabled = false;
                    chunk.Collider.sharedMesh = null;
                }
                if (chunk.Mesh) Destroy(chunk.Mesh);
            }
            var surface = GetComponent<MeshRenderer>();
            if (surface) surface.enabled = originalSurfaceEnabled;
        }
        if (container)
        {
            container.gameObject.SetActive(false);
            Destroy(container.gameObject);
        }
        container = null;
        chunks = null;
        visuals = null;
        sources = null;
    }
}
