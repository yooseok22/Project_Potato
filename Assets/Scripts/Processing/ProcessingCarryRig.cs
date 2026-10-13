using System.Collections.Generic;
using UnityEngine;

/// <summary>Moves the original skinned renderers with private copies of their posed bones.</summary>
[DisallowMultipleComponent]
public sealed class ProcessingCarryRig : MonoBehaviour
{
    [SerializeField] private Transform sourceBoneRoot;
    [SerializeField] private SkinnedMeshRenderer[] renderers;
    private sealed class RendererHome
    {
        public Transform parent;
        public Vector3 position, scale;
        public Quaternion rotation;
        public Transform[] bones;
        public Transform rootBone;
    }

    private RendererHome[] homes;
    private Transform copiedRoot;
    public bool IsDetached => copiedRoot;

    public void Configure(Transform boneRoot, SkinnedMeshRenderer[] meshes)
    {
        sourceBoneRoot = boneRoot;
        renderers = meshes;
    }

    public void Detach()
    {
        if (copiedRoot || !sourceBoneRoot) return;
        homes = new RendererHome[renderers.Length];
        var map = new Dictionary<Transform, Transform>();
        copiedRoot = CopyBone(sourceBoneRoot, transform, map);
        for (int index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            homes[index] = new RendererHome { parent = renderer.transform.parent,
                position = renderer.transform.localPosition, rotation = renderer.transform.localRotation,
                scale = renderer.transform.localScale, bones = renderer.bones, rootBone = renderer.rootBone };
            var bones = new Transform[renderer.bones.Length];
            for (int bone = 0; bone < bones.Length; bone++) bones[bone] = map[renderer.bones[bone]];
            renderer.transform.SetParent(transform, true);
            renderer.bones = bones;
            renderer.rootBone = map[renderer.rootBone];
            renderer.updateWhenOffscreen = true;
        }
    }

    private static Transform CopyBone(Transform source, Transform parent, Dictionary<Transform, Transform> map)
    {
        var copy = new GameObject(source.name).transform;
        copy.SetParent(source.parent, false);
        copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation;
        copy.localScale = source.localScale;
        copy.SetParent(parent, true);
        map.Add(source, copy);
        foreach (Transform child in source) CopyBone(child, copy, map);
        return copy;
    }

    public void Restore()
    {
        if (!copiedRoot) return;
        for (int index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            var home = homes[index];
            renderer.transform.SetParent(home.parent, false);
            renderer.transform.localPosition = home.position;
            renderer.transform.localRotation = home.rotation;
            renderer.transform.localScale = home.scale;
            renderer.bones = home.bones;
            renderer.rootBone = home.rootBone;
        }
        copiedRoot.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(copiedRoot.gameObject);
        else DestroyImmediate(copiedRoot.gameObject);
        copiedRoot = null;
        homes = null;
    }

    private void OnDestroy() => Restore();
}
