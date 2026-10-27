using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PotatoPackingBoxSetup
{
    private const string ModelPath = "Assets/Fbx/PotatoPack/Potato Millionaire Box.fbx";
    private const string MaterialPath = "Assets/Materials/BoxPackingPreview.mat";

    [MenuItem("Tools/Project Potato/Setup Packing Box")]
    public static void SetupCurrentScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode before setting up the box.");
        var scene = SceneManager.GetActiveScene();
        var objects = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        GameObject Find(string name) => objects.SingleOrDefault(t => t.name == name)?.gameObject
            ?? throw new InvalidOperationException("Missing scene object: " + name);
        var box = Find("Potato Millionaire Box");
        var potatoes = Find("Potato Millionaire Potato Pack Location").transform;
        var starch = Find("Potato Millionaire Starch Pack Location").transform;
        var potatoSlots = FindSlots(potatoes, "FBX_Potato Pack Location ", 8);
        var starchSlots = FindSlots(starch, "FBX_Starch Pack Location ", 20);
        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().ToArray();
        var folding = clips.Single(c => c.name == "Anim_Box Folding");
        var packing = clips.Single(c => c.name == "Anim_Box Packing");
        var shader = Shader.Find("Project Potato/Packing Preview");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Packing preview shader is missing or invalid.");

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Connect potato and starch packing box");
        Undo.RecordObjects(box.GetComponentsInChildren<Transform>(true).Cast<UnityEngine.Object>().ToArray(), "Configure box pose");
        if (potatoes.parent != box.transform) Undo.SetTransformParent(potatoes, box.transform, "Parent potato locations");
        if (starch.parent != box.transform) Undo.SetTransformParent(starch, box.transform, "Parent starch locations");
        Undo.RecordObjects(new UnityEngine.Object[] { potatoes.gameObject, starch.gameObject }
            .Concat(potatoSlots.Concat(starchSlots).Select(t => (UnityEngine.Object)t.gameObject)).ToArray(), "Hide packing contents");
        var driver = GetOrAdd<PackingBoxAnimationDriver>(box);
        var packingBox = GetOrAdd<PotatoPackingBox>(box);
        var collider = GetOrAdd<BoxCollider>(box);
        Undo.RecordObjects(new UnityEngine.Object[] { driver, packingBox, collider }, "Configure packing behaviour");
        var animator = box.GetComponent<Animator>();
        if (animator) Undo.RecordObject(animator, "Disable automatic box animation");
        driver.Configure(animator, BuildPose(box.transform, folding), BuildPose(box.transform, packing));
        var flatBounds = MeshBounds(box.transform);
        driver.SetPose(driver.FoldLength, 0f);
        var assembledBounds = MeshBounds(box.transform);
        driver.SetPose(0f, 0f);
        collider.isTrigger = true;

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            material = new Material(shader) { name = "Box Packing Preview" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetColor("_BaseColor", new Color(0.12f, 1f, 0.3f, 0.18f));
        EditorUtility.SetDirty(material);
        packingBox.Configure(driver, potatoes, starch, potatoSlots, starchSlots, material, collider, flatBounds, assembledBounds);
        foreach (var component in box.GetComponentsInChildren<Component>(true))
        {
            if (!component) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorUtility.SetDirty(component);
        }
        foreach (var go in potatoSlots.Concat(starchSlots).Select(t => t.gameObject)
            .Concat(new[] { potatoes.gameObject, starch.gameObject }))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            EditorUtility.SetDirty(go);
        }
        Undo.CollapseUndoOperations(undoGroup);
        AssetDatabase.SaveAssetIfDirty(material);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = box;
        Debug.Log("[PotatoPackingBox] Connected: folding, packing pulse, 8 potatoes / 20 starch packs, green placement preview.", box);
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
        => go.GetComponent<T>() ? go.GetComponent<T>() : Undo.AddComponent<T>(go);

    private static Transform[] FindSlots(Transform group, string prefix, int count)
    {
        var slots = group.Cast<Transform>().Where(t => t.name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(t => int.Parse(t.name.Substring(prefix.Length))).ToArray();
        if (slots.Length != count || slots.Where((t, index) => t.name != prefix + (index + 1)).Any())
            throw new InvalidOperationException($"{group.name} must have sequential Location 1 through {count}.");
        if (slots.Any(t => !t.GetComponent<MeshFilter>() || !t.GetComponent<MeshRenderer>()))
            throw new InvalidOperationException("Each packing location needs a mesh for the ghost preview.");
        return slots;
    }

    private static PackingBoxAnimationDriver.ClipPose BuildPose(Transform root, AnimationClip clip)
    {
        var tracks = new Dictionary<string, PackingBoxAnimationDriver.TransformTrack>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Transform)) throw new InvalidOperationException("Unsupported animation binding: " + binding.type);
            if (!tracks.TryGetValue(binding.path, out var track))
            {
                var target = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                if (!target) throw new InvalidOperationException("Missing animation target: " + binding.path);
                track = new PackingBoxAnimationDriver.TransformTrack { target = target };
                tracks.Add(binding.path, track);
            }
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            switch (binding.propertyName)
            {
                case "m_LocalPosition.x": track.px = curve; break;
                case "m_LocalPosition.y": track.py = curve; break;
                case "m_LocalPosition.z": track.pz = curve; break;
                case "m_LocalRotation.x": track.rx = curve; break;
                case "m_LocalRotation.y": track.ry = curve; break;
                case "m_LocalRotation.z": track.rz = curve; break;
                case "m_LocalRotation.w": track.rw = curve; break;
                case "m_LocalScale.x": track.sx = curve; break;
                case "m_LocalScale.y": track.sy = curve; break;
                case "m_LocalScale.z": track.sz = curve; break;
                default: throw new InvalidOperationException("Unsupported animation channel: " + binding.propertyName);
            }
        }
        return new PackingBoxAnimationDriver.ClipPose { source = clip, tracks = tracks.Values.ToArray() };
    }

    private static Bounds MeshBounds(Transform root)
    {
        // Bake the skin: SkinnedMeshRenderer.bounds can retain the flat import bounds after folding.
        var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
        if (!renderer) throw new InvalidOperationException("The packing box needs its skinned box mesh.");
        var mesh = new Mesh();
        try
        {
            renderer.BakeMesh(mesh);
            var vertices = mesh.vertices;
            var bounds = new Bounds(root.InverseTransformPoint(renderer.transform.TransformPoint(vertices[0])), Vector3.zero);
            foreach (var vertex in vertices)
                bounds.Encapsulate(root.InverseTransformPoint(renderer.transform.TransformPoint(vertex)));
            var size = bounds.size;
            size.y = Mathf.Max(0.12f, size.y);
            bounds.size = size;
            return bounds;
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
}
