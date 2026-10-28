using System.Collections.Generic;
using UnityEngine;

/// <summary>Walk-through supply line. Pinned waypoints consume the same finite cable length.</summary>
public sealed class AutomationLine : MonoBehaviour
{
    public AutomationBlock Source { get; private set; }
    public AutomationBlock Target { get; private set; }
    public int Outlet { get; private set; }
    public IReadOnlyList<Vector3> Anchors => anchors;
    public bool IsConnected => Source && Target;
    public bool IsLive => IsConnected && Source.isActiveAndEnabled && Target.isActiveAndEnabled &&
        RouteLength(Target.PortPosition) <= Source.MaximumLineLength + 0.001f;
    private readonly List<Vector3> anchors = new();
    private LineRenderer line;
    private Material material;
    private Vector3 looseEnd;
    private readonly List<Vector3> renderedPoints = new();
    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private float nextRenderAt;

    public void Initialize(AutomationBlock source, int outlet)
    {
        Source = source;
        Outlet = outlet;
        looseEnd = source.PortPosition;
        line = gameObject.AddComponent<LineRenderer>();
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        material.color = source.SupplyKind == AutomationSupplyKind.Water ? new Color(0.15f, 0.5f, 0.9f) : new Color(0.95f, 0.7f, 0.1f);
        line.sharedMaterial = material;
        line.startWidth = line.endWidth = source.SupplyKind == AutomationSupplyKind.Water ? 0.055f : 0.035f;
        line.numCapVertices = 3;
        line.numCornerVertices = 3;
        line.useWorldSpace = true;
    }

    public float RouteLength(Vector3 end)
    {
        if (!Source) return float.PositiveInfinity;
        Vector3 previous = Source.PortPosition;
        float length = 0;
        foreach (var anchor in anchors) { length += Vector3.Distance(previous, anchor); previous = anchor; }
        return length + Vector3.Distance(previous, end);
    }

    public bool TryPin(Vector3 point)
    {
        if (!Source || Target || anchors.Count >= 64 || RouteLength(point) > Source.MaximumLineLength) return false;
        Vector3 previous = anchors.Count > 0 ? anchors[anchors.Count - 1] : Source.PortPosition;
        if (Vector3.Distance(previous, point) < 0.15f) return false;
        anchors.Add(point);
        return true;
    }

    public void RemoveLastAnchor() { if (anchors.Count > 0) anchors.RemoveAt(anchors.Count - 1); }

    public bool TryConnect(AutomationBlock target)
    {
        if (!Source || Target || !target || !target.Accepts(Source.SupplyKind) ||
            RouteLength(target.PortPosition) > Source.MaximumLineLength) return false;
        Target = target;
        return true;
    }

    public void SetLooseEnd(Vector3 point)
    {
        if (!Source) return;
        Vector3 start = anchors.Count > 0 ? anchors[anchors.Count - 1] : Source.PortPosition;
        float available = Mathf.Max(0f, Source.MaximumLineLength - RouteLength(start));
        looseEnd = start + Vector3.ClampMagnitude(point - start, available);
    }

    private void LateUpdate()
    {
        if (!Source || !line || Time.time < nextRenderAt) return;
        nextRenderAt = Time.time + 0.1f;
        renderedPoints.Clear();
        Vector3 previous = Source.PortPosition;
        for (int span = 0; span <= anchors.Count; span++)
        {
            Vector3 end = span < anchors.Count ? anchors[span] : Target ? Target.PortPosition : looseEnd;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(previous, end) / 0.2f), 2, 120);
            for (int index = 0; index < steps; index++)
            {
                float t = (float)index / steps;
                Vector3 point = Vector3.Lerp(previous, end, t);
                float slack = Mathf.Sin(t * Mathf.PI);
                point.y -= slack * 0.65f;
                point += Vector3.Cross((end - previous).normalized, Vector3.up) *
                    (Mathf.Sin(t * 8f + Time.time * 1.2f) * 0.035f * slack);
                // Rest loose spans on terrain; lines never have colliders.
                int hitCount = Physics.RaycastNonAlloc(point + Vector3.up * 2f, Vector3.down, groundHits, 4f, ~0, QueryTriggerInteraction.Ignore);
                for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                {
                    var hit = groundHits[hitIndex];
                    if (hit.collider.GetComponentInParent<AutomationBlock>() || hit.collider.GetComponentInParent<FirstPersonPlayerController>() ||
                        hit.collider.attachedRigidbody || hit.point.y > Mathf.Max(previous.y, end.y) + 0.1f) continue;
                    point.y = Mathf.Max(point.y, hit.point.y + 0.035f);
                }
                renderedPoints.Add(point);
            }
            previous = end;
        }
        renderedPoints.Add(previous);
        line.positionCount = renderedPoints.Count;
        line.SetPositions(renderedPoints.ToArray());
    }

    private void OnDestroy() => AutomationWorld.DestroyOwned(material);
}
