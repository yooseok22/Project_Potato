using System.Collections.Generic;
using UnityEngine;

// Cached complete authored tile used by the V2 chunk renderer.
internal sealed class TileMeshSource
{
    private struct Part
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] UV;
        public int[][] Triangles;
        public int[] MaterialIndices;
    }

    private readonly List<Part> parts = new List<Part>();

    public static TileMeshSource Create(GameObject prefab, List<Material> materials)
    {
        var source = new TileMeshSource();
        var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0) throw new System.InvalidOperationException(prefab.name + " has no MeshFilter.");

        // Preserve the prefab's authored rotation and scale, but discard its
        // gallery/world position so every tile is centred on its grid cell.
        var rootPose = Matrix4x4.TRS(Vector3.zero, prefab.transform.localRotation, prefab.transform.localScale);
        foreach (var filter in filters)
        {
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!mesh || !renderer) continue;
            if (!mesh.isReadable) throw new System.InvalidOperationException(mesh.name + " is not readable.");

            var matrix = rootPose * prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            var positions = mesh.vertices;
            var sourceNormals = mesh.normals;
            var sourceUV = mesh.uv;
            var vertices = new Vector3[positions.Length];
            var normals = new Vector3[positions.Length];
            var uv = new Vector2[positions.Length];
            for (int index = 0; index < positions.Length; index++)
            {
                vertices[index] = matrix.MultiplyPoint3x4(positions[index]);
                normals[index] = sourceNormals.Length == positions.Length
                    ? normalMatrix.MultiplyVector(sourceNormals[index]).normalized
                    : Vector3.up;
                uv[index] = sourceUV.Length == positions.Length ? sourceUV[index] : Vector2.zero;
            }

            var triangles = new int[mesh.subMeshCount][];
            var materialIndices = new int[mesh.subMeshCount];
            var sourceMaterials = renderer.sharedMaterials;
            if (sourceMaterials.Length == 0)
                throw new System.InvalidOperationException(prefab.name + " has no materials.");
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                triangles[submesh] = mesh.GetTriangles(submesh);
                var material = sourceMaterials[Mathf.Min(submesh, sourceMaterials.Length - 1)];
                int materialIndex = materials.IndexOf(material);
                if (materialIndex < 0) { materialIndex = materials.Count; materials.Add(material); }
                materialIndices[submesh] = materialIndex;
            }

            source.parts.Add(new Part
            {
                Vertices = vertices,
                Normals = normals,
                UV = uv,
                Triangles = triangles,
                MaterialIndices = materialIndices
            });
        }
        return source;
    }

    public void Append(List<Vector3> outputVertices, List<Vector3> outputNormals, List<Vector2> outputUV,
        List<int>[] outputTriangles, Vector3 offset, float scale, int quarterTurns)
    {
        var rotation = Quaternion.Euler(0f, quarterTurns * 90f, 0f);
        foreach (var part in parts)
        {
            int vertexStart = outputVertices.Count;
            for (int index = 0; index < part.Vertices.Length; index++)
            {
                outputVertices.Add(offset + rotation * (part.Vertices[index] * scale));
                outputNormals.Add(rotation * part.Normals[index]);
                outputUV.Add(part.UV[index]);
            }
            for (int submesh = 0; submesh < part.Triangles.Length; submesh++)
            {
                var destination = outputTriangles[part.MaterialIndices[submesh]];
                foreach (int index in part.Triangles[submesh]) destination.Add(vertexStart + index);
            }
        }
    }
}
