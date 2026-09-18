using System.Collections.Generic;
using UnityEngine;

// Cached quarter of an authored mesh. No GameObjects or temporary Unity meshes are needed.
internal sealed class TileMeshPatch
{
    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
        {
            Position = Vector3.LerpUnclamped(a.Position, b.Position, t),
            Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t).normalized,
            UV = Vector2.LerpUnclamped(a.UV, b.UV, t)
        };
    }
    private readonly List<Vertex> vertices = new List<Vertex>();
    private readonly List<int> triangleMaterials = new List<int>();

    public static TileMeshPatch Create(GameObject prefab, int turns, int quadrant, List<Material> materials)
    {
        var patch = new TileMeshPatch();
        var rotation = Matrix4x4.Rotate(Quaternion.Euler(0f, turns * 90f, 0f));
        float xSign = quadrant < 2 ? -1f : 1f;
        float zSign = quadrant == 0 || quadrant == 3 ? -1f : 1f;
        var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0) throw new System.InvalidOperationException(prefab.name + " has no MeshFilter.");
        foreach (var filter in filters)
        {
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!mesh || !renderer) continue;
            if (!mesh.isReadable) throw new System.InvalidOperationException(mesh.name + " is not readable.");
            // Strip saved root position/rotation/scale, preserving only child mesh transforms.
            var matrix = rotation * prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var uv = mesh.uv;
            var sourceMaterials = renderer.sharedMaterials;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (sourceMaterials.Length == 0) throw new System.InvalidOperationException(prefab.name + " has no materials.");
                var material = sourceMaterials[Mathf.Min(submesh, sourceMaterials.Length - 1)];
                int materialIndex = materials.IndexOf(material);
                if (materialIndex < 0) { materialIndex = materials.Count; materials.Add(material); }
                var triangles = mesh.GetTriangles(submesh);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    var polygon = new List<Vertex>(3);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int index = triangles[t + corner];
                        var p = matrix.MultiplyPoint3x4(positions[index]);
                        if (Mathf.Abs(p.x) < 0.00001f) p.x = 0f;
                        if (Mathf.Abs(p.z) < 0.00001f) p.z = 0f;
                        polygon.Add(new Vertex
                        {
                            Position = p,
                            Normal = normals.Length == positions.Length ? normalMatrix.MultiplyVector(normals[index]).normalized : Vector3.up,
                            UV = uv.Length == positions.Length ? uv[index] : Vector2.zero
                        });
                    }
                    polygon = Clip(polygon, true, xSign);
                    polygon = Clip(polygon, false, zSign);
                    for (int corner = 1; corner + 1 < polygon.Count; corner++)
                    {
                        var a = polygon[0]; var b = polygon[corner]; var c = polygon[corner + 1];
                        if (Vector3.Cross(b.Position - a.Position, c.Position - a.Position).sqrMagnitude < 0.0000000001f) continue;
                        patch.vertices.Add(a); patch.vertices.Add(b); patch.vertices.Add(c);
                        patch.triangleMaterials.Add(materialIndex);
                    }
                }
            }
        }
        return patch;
    }

    // Sutherland-Hodgman clipping against the centre X/Z planes.
    private static List<Vertex> Clip(List<Vertex> input, bool xAxis, float sign)
    {
        var output = new List<Vertex>();
        if (input.Count == 0) return output;
        var previous = input[input.Count - 1];
        float previousDistance = (xAxis ? previous.Position.x : previous.Position.z) * sign;
        foreach (var current in input)
        {
            float currentDistance = (xAxis ? current.Position.x : current.Position.z) * sign;
            if ((currentDistance >= 0f) != (previousDistance >= 0f))
            {
                float t = previousDistance / (previousDistance - currentDistance);
                var intersection = Vertex.Lerp(previous, current, t);
                if (xAxis) intersection.Position.x = 0f; else intersection.Position.z = 0f;
                output.Add(intersection);
            }
            if (currentDistance >= 0f) output.Add(current);
            previous = current;
            previousDistance = currentDistance;
        }
        return output;
    }

    public void Append(List<Vector3> outputVertices, List<Vector3> outputNormals, List<Vector2> outputUV,
        List<int>[] outputTriangles, Vector3 offset, float scale)
    {
        for (int triangle = 0; triangle < triangleMaterials.Count; triangle++)
        {
            var indices = outputTriangles[triangleMaterials[triangle]];
            for (int corner = 0; corner < 3; corner++)
            {
                var vertex = vertices[triangle * 3 + corner];
                indices.Add(outputVertices.Count);
                outputVertices.Add(offset + vertex.Position * scale);
                outputNormals.Add(vertex.Normal);
                outputUV.Add(vertex.UV);
            }
        }
    }
}
