using UnityEngine;

public sealed class FarmingPreviewView : MonoBehaviour
{
    [SerializeField] private Renderer fill;
    [SerializeField] private LineRenderer outline;
    [SerializeField] private Color validColor = new Color(0.2f, 1f, 0.4f, 1f);
    [SerializeField] private Color invalidColor = new Color(1f, 0.25f, 0.18f, 1f);
    [SerializeField, Range(0f, 1f)] private float fillOpacity = 0.2f;
    [SerializeField, Min(0.001f)] private float surfaceOffset = 0.02f;
    private MaterialPropertyBlock properties;

    public void Configure(Renderer fillRenderer, LineRenderer line)
    {
        fill = fillRenderer;
        outline = line;
        Hide();
    }

    public void Show(Vector3 center, float size, bool valid)
    {
        transform.position = center + Vector3.up * surfaceOffset;
        transform.localScale = new Vector3(size, 1f, size);
        Color color = valid ? validColor : invalidColor;
        if (fill)
        {
            properties ??= new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", new Color(color.r, color.g, color.b, fillOpacity));
            fill.SetPropertyBlock(properties);
            fill.enabled = true;
        }
        if (outline)
        {
            outline.startColor = outline.endColor = color;
            outline.enabled = true;
        }
    }

    public void Hide()
    {
        if (fill) fill.enabled = false;
        if (outline) outline.enabled = false;
    }

    private void OnDisable() => Hide();
}
