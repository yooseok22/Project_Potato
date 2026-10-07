using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(GroundGrid))]
public sealed class GroundTileRenderer : MonoBehaviour
{
    private const int TileKindCount = 45;

    [Header("Final complete-cell tiles (N, G, GB, GN)")]
    [SerializeField] private GameObject[] tilePrefabs = new GameObject[TileKindCount];
    [SerializeField, Min(0.01f)] private float prefabCellSize = 2f;
    [SerializeField, Range(1, 64)] private int chunkSize = 16;

    private sealed class Chunk
    {
        public Mesh Mesh;
        public MeshFilter Filter;
        public MeshCollider Collider;
        public Mesh WetMesh;
        public MeshFilter WetFilter;
    }

    private GroundGrid grid;
    private Chunk[,] chunks;
    private TileVisualResolver.Visual[,] visuals;
    private TileMeshSource[] sources;
    private bool[,] watered;
    private readonly List<Material> materials = new List<Material>();
    private Transform container;
    private bool originalSurfaceEnabled;
    private bool started;
    private Material wetMaterial;

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
        // Older scenes serialized the original 44 entries. Keep them usable until their
        // dedicated GD F prefab is assigned; the field-soil mesh is a safe visual fallback.
        if (tilePrefabs != null && tilePrefabs.Length == TileKindCount - 1)
        {
            System.Array.Resize(ref tilePrefabs, TileKindCount);
            tilePrefabs[TileKindCount - 1] = tilePrefabs[(int)GroundCellState.FieldSoil];
        }
        if (tilePrefabs == null || tilePrefabs.Length != TileKindCount)
        {
            Debug.LogError("Assign exactly forty-five final tile prefabs in GroundCellState order.", this);
            return;
        }
        foreach (var prefab in tilePrefabs)
        {
            if (prefab) continue;
            Debug.LogError("Assign all forty-five final tile prefabs in GroundCellState order.", this);
            return;
        }

        chunkSize = Mathf.Clamp(chunkSize, 1, 64);
        materials.Clear();
        try
        {
            sources = new TileMeshSource[TileKindCount];
            for (int index = 0; index < sources.Length; index++)
                sources[index] = TileMeshSource.Create(tilePrefabs[index], materials);
        }
        catch (System.Exception exception)
        {
            Debug.LogError("Unable to read a final tile mesh. Enable Read/Write on its FBX importer. " + exception.Message, this);
            sources = null;
            return;
        }

        visuals = new TileVisualResolver.Visual[grid.Width, grid.Depth];
        watered = new bool[grid.Width, grid.Depth];
        for (int x = 0; x < grid.Width; x++)
            for (int z = 0; z < grid.Depth; z++)
            {
                visuals[x, z] = TileVisualResolver.Resolve(grid, new Vector2Int(x, z));
                watered[x, z] = grid.IsWatered(new Vector2Int(x, z));
            }

        var wetShader = Shader.Find("Project Potato/Wet Soil Overlay");
        if (!wetShader)
        {
            Debug.LogError("Wet soil overlay shader is missing.", this);
            return;
        }
        wetMaterial = new Material(wetShader) { name = "Wet Soil Overlay (Runtime)" };
        wetMaterial.SetColor("_BaseColor", new Color(0.08f, 0.12f, 0.04f, 0.38f));

        container = new GameObject("GroundChunks").transform;
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
                var go = new GameObject($"Chunk_{x}_{z}", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                go.layer = gameObject.layer;
                go.transform.SetParent(container, false);
                go.transform.position = grid.Origin + new Vector3(x * chunkSize * grid.CellSize, 0f, z * chunkSize * grid.CellSize);
                go.GetComponent<MeshRenderer>().sharedMaterials = materials.ToArray();

                var mesh = new Mesh { name = $"GroundChunk_{x}_{z}", indexFormat = IndexFormat.UInt32 };
                mesh.MarkDynamic();
                var wetGo = new GameObject("WetOverlay", typeof(MeshFilter), typeof(MeshRenderer));
                wetGo.layer = gameObject.layer;
                wetGo.transform.SetParent(go.transform, false);
                var wetMesh = new Mesh { name = $"WetGroundChunk_{x}_{z}", indexFormat = IndexFormat.UInt32 };
                wetMesh.MarkDynamic();
                var wetFilter = wetGo.GetComponent<MeshFilter>();
                wetFilter.sharedMesh = wetMesh;
                wetGo.GetComponent<MeshRenderer>().sharedMaterial = wetMaterial;
                var chunk = new Chunk
                {
                    Mesh = mesh,
                    Filter = go.GetComponent<MeshFilter>(),
                    Collider = go.GetComponent<MeshCollider>(),
                    WetMesh = wetMesh,
                    WetFilter = wetFilter
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

    public TileVisualResolver.Visual GetVisual(Vector2Int cell)
    {
        if (!grid || !grid.ContainsCell(cell)) return default;
        return TileVisualResolver.Resolve(grid, cell);
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
                var visual = TileVisualResolver.Resolve(grid, target);
                bool isWatered = grid.IsWatered(target);
                if (visuals[target.x, target.y].SameAs(visual) && watered[target.x, target.y] == isWatered) continue;
                visuals[target.x, target.y] = visual;
                watered[target.x, target.y] = isWatered;
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
        var wetVertices = new List<Vector3>();
        var wetNormals = new List<Vector3>();
        var wetUV = new List<Vector2>();
        var wetTriangles = new List<int>();

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
                sources[visual.Kind].Append(vertices, normals, uv, triangles, offset, scale,
                    visual.QuarterTurns, visual.MirrorX);
                if (watered[x, z])
                    sources[visual.Kind].AppendOverlay(wetVertices, wetNormals, wetUV, wetTriangles,
                        offset, scale, visual.QuarterTurns, visual.MirrorX, 0.006f);
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

        var wetMesh = chunk.WetMesh;
        wetMesh.Clear();
        wetMesh.SetVertices(wetVertices);
        wetMesh.SetNormals(wetNormals);
        wetMesh.SetUVs(0, wetUV);
        wetMesh.SetTriangles(wetTriangles, 0, false);
        wetMesh.RecalculateBounds();
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
                if (chunk.WetMesh) Destroy(chunk.WetMesh);
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
        watered = null;
        sources = null;
        if (wetMaterial) Destroy(wetMaterial);
        wetMaterial = null;
    }
}
