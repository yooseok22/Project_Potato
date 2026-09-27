using System.Collections.Generic;
using UnityEngine;

public sealed class InteractionOutline : MonoBehaviour, IInteractionHighlight
{
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private Color color = new Color(1f, 0.82f, 0.2f, 1f);
    [SerializeField, Range(0.001f, 0.05f)] private float width = 0.012f;

    private readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
    private Material runtimeMaterial;

    private void Awake()
    {
        if (targetRenderers == null || targetRenderers.Length == 0)
            targetRenderers = GetComponentsInChildren<Renderer>(true);
    }

    public void SetHighlighted(bool highlighted)
    {
        if (highlighted) EnableOutline();
        else DisableOutline();
    }

    private void EnableOutline()
    {
        if (!runtimeMaterial)
        {
            var shader = Shader.Find("Project Potato/Interaction Outline");
            if (!shader) return;
            runtimeMaterial = new Material(shader) { name = "Interaction Outline (Runtime)" };
            runtimeMaterial.SetColor("_OutlineColor", color);
            runtimeMaterial.SetFloat("_OutlineWidth", width);
        }
        foreach (var renderer in targetRenderers)
        {
            if (!renderer || originals.ContainsKey(renderer)) continue;
            var original = renderer.sharedMaterials;
            originals.Add(renderer, original);
            var outlined = new Material[original.Length + 1];
            original.CopyTo(outlined, 0);
            outlined[outlined.Length - 1] = runtimeMaterial;
            renderer.sharedMaterials = outlined;
        }
    }

    private void DisableOutline()
    {
        foreach (var pair in originals)
            if (pair.Key) pair.Key.sharedMaterials = pair.Value;
        originals.Clear();
    }

    private void OnDisable() => DisableOutline();
    private void OnDestroy()
    {
        DisableOutline();
        if (runtimeMaterial) Destroy(runtimeMaterial);
    }
}
