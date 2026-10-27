using System;
using UnityEngine;
using UnityEngine.Rendering;

public enum BoxPackingContent { None, Potato, Starch }

[DisallowMultipleComponent]
[RequireComponent(typeof(PackingBoxAnimationDriver))]
public sealed class PotatoPackingBox : MonoBehaviour, IPlayerInteractable, IInteractionHighlight
{
    private enum Phase { Unfolded, Folding, Ready, Packing, Complete }

    [SerializeField] private PackingBoxAnimationDriver animationDriver;
    [SerializeField] private Transform potatoGroup;
    [SerializeField] private Transform starchGroup;
    [SerializeField] private Transform[] potatoLocations = Array.Empty<Transform>();
    [SerializeField] private Transform[] starchLocations = Array.Empty<Transform>();
    [SerializeField] private Material previewMaterial;
    [SerializeField] private BoxCollider interactionCollider;
    [SerializeField] private Bounds unfoldedBounds;
    [SerializeField] private Bounds foldedBounds;

    private Phase phase;
    private float phaseTime;
    private bool highlighted;
    private BoxPackingContent previewSelection;
    private Transform previewSlot;
    private GameObject preview;

    public BoxPackingContent LockedContent { get; private set; }
    public int PackedCount { get; private set; }
    public int PackingPulseCount { get; private set; }
    public int Capacity => Locations(LockedContent == BoxPackingContent.None ? previewSelection : LockedContent).Length;
    public bool IsFolded => phase != Phase.Unfolded && phase != Phase.Folding;
    public bool IsBusy => phase == Phase.Folding || phase == Phase.Packing;
    public bool IsComplete => phase == Phase.Complete;
    public Transform PreviewLocation => preview && preview.activeSelf ? previewSlot : null;
    public bool RequiresHold => false;
    public float HoldDuration => 0f;

    public string Prompt
    {
        get
        {
            if (phase == Phase.Unfolded) return "[E] 박스 접기  |  [1] 감자  [2] 감자전분";
            if (phase == Phase.Folding) return "박스 접는 중…";
            if (IsComplete) return $"{ContentName(LockedContent)} 포장 완료 ({PackedCount}/{Capacity})";
            if (phase == Phase.Packing) return "박스 포장 중…";
            if (LockedContent != BoxPackingContent.None && previewSelection != LockedContent)
                return $"{ContentName(LockedContent)} 전용 박스 ({PackedCount}/{Capacity})  |  [{(int)LockedContent}] 선택";
            if (previewSelection == BoxPackingContent.None) return "포장 선택: [1] 감자  [2] 감자전분";
            return $"[E] {ContentName(previewSelection)} 넣기 ({PackedCount}/{Capacity})" +
                   (LockedContent == BoxPackingContent.None ? "  |  [1] 감자  [2] 감자전분" : string.Empty);
        }
    }

    private static string ContentName(BoxPackingContent content)
        => content == BoxPackingContent.Potato ? "감자" : "감자전분";

    private Transform[] Locations(BoxPackingContent content) => content switch
    {
        BoxPackingContent.Potato => potatoLocations,
        BoxPackingContent.Starch => starchLocations,
        _ => Array.Empty<Transform>()
    };

    public void Configure(PackingBoxAnimationDriver driver, Transform potatoes, Transform starch,
        Transform[] potatoSlots, Transform[] starchSlots, Material ghostMaterial, BoxCollider collider,
        Bounds flatBounds, Bounds assembledBounds)
    {
        animationDriver = driver;
        potatoGroup = potatoes;
        starchGroup = starch;
        potatoLocations = potatoSlots;
        starchLocations = starchSlots;
        previewMaterial = ghostMaterial;
        interactionCollider = collider;
        unfoldedBounds = flatBounds;
        foldedBounds = assembledBounds;
        ResetPackaging();
    }

    private void Awake()
    {
        if (!animationDriver) animationDriver = GetComponent<PackingBoxAnimationDriver>();
        ResetPackaging();
    }

    public void ResetPackaging()
    {
        phase = Phase.Unfolded;
        phaseTime = 0f;
        PackedCount = PackingPulseCount = 0;
        LockedContent = previewSelection = BoxPackingContent.None;
        highlighted = false;
        foreach (var slot in potatoLocations) if (slot) slot.gameObject.SetActive(false);
        foreach (var slot in starchLocations) if (slot) slot.gameObject.SetActive(false);
        if (potatoGroup) potatoGroup.gameObject.SetActive(false);
        if (starchGroup) starchGroup.gameObject.SetActive(false);
        if (animationDriver) animationDriver.SetPose(0f, 0f);
        SetColliderBounds(unfoldedBounds);
        ClearPreview();
    }

    public bool CanInteract(PlayerInteractionController player)
    {
        if (!isActiveAndEnabled || !player || IsBusy || IsComplete || !animationDriver) return false;
        if (!IsFolded) return true;
        var selected = player.SelectedPackingContent;
        return selected != BoxPackingContent.None && Locations(selected).Length > PackedCount &&
               (LockedContent == BoxPackingContent.None || LockedContent == selected);
    }

    public void Interact(PlayerInteractionController player)
    {
        if (!CanInteract(player)) return;
        SetPreviewSelection(player.SelectedPackingContent);
        if (!IsFolded)
        {
            phase = Phase.Folding;
            phaseTime = 0f;
            animationDriver.SetPose(0f, 0f);
            RefreshPreview();
            return;
        }

        if (LockedContent == BoxPackingContent.None) LockedContent = player.SelectedPackingContent;
        var group = LockedContent == BoxPackingContent.Potato ? potatoGroup : starchGroup;
        group.gameObject.SetActive(true);
        Locations(LockedContent)[PackedCount].gameObject.SetActive(true);
        PackedCount++;
        BeginPackingPulse();
    }

    private void BeginPackingPulse()
    {
        phase = Phase.Packing;
        phaseTime = 0f;
        PackingPulseCount++;
        animationDriver.SetPose(animationDriver.FoldLength, 0f);
        RefreshPreview();
    }

    private void Update() => AdvanceAnimation(Time.deltaTime);

    /// <summary>Advances the one-shot animations and retains the final assembled pose.</summary>
    public void AdvanceAnimation(float seconds)
    {
        if (!animationDriver || !IsBusy || seconds <= 0f) return;
        while (IsBusy)
        {
            float length = phase == Phase.Folding ? animationDriver.FoldLength : animationDriver.PackingLength;
            float step = Mathf.Min(seconds, Mathf.Max(0f, length - phaseTime));
            phaseTime += step;
            seconds -= step;
            animationDriver.SetPose(phase == Phase.Folding ? phaseTime : animationDriver.FoldLength,
                phase == Phase.Packing ? phaseTime : 0f);
            if (phaseTime < length) break;
            if (phase == Phase.Folding)
            {
                SetColliderBounds(foldedBounds);
                BeginPackingPulse();
            }
            else
            {
                phase = PackedCount > 0 && PackedCount == Locations(LockedContent).Length ? Phase.Complete : Phase.Ready;
                animationDriver.SetPose(animationDriver.FoldLength, animationDriver.PackingLength);
                RefreshPreview();
            }
            if (seconds <= 0f) break;
        }
    }

    private void SetColliderBounds(Bounds bounds)
    {
        if (!interactionCollider) return;
        interactionCollider.center = bounds.center;
        interactionCollider.size = bounds.size;
    }

    public void SetHighlighted(bool value)
    {
        highlighted = value;
        RefreshPreview();
    }

    public void SetPreviewSelection(BoxPackingContent content)
    {
        previewSelection = content;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var content = LockedContent == BoxPackingContent.None ? previewSelection : LockedContent;
        var slots = Locations(content);
        bool visible = isActiveAndEnabled && highlighted && IsFolded && PackedCount < slots.Length;
        if (!visible || !previewMaterial)
        {
            if (preview) preview.SetActive(false);
            return;
        }
        var next = slots[PackedCount];
        if (previewSlot != next || !preview)
        {
            ClearPreview();
            previewSlot = next;
            var group = content == BoxPackingContent.Potato ? potatoGroup : starchGroup;
            preview = new GameObject("Packing Preview (Runtime)") { layer = 2, hideFlags = HideFlags.DontSave };
            preview.transform.SetParent(transform, false);
            CopyTransform(group, preview.transform);
            CopyPreviewMeshes(next, preview.transform);
        }
        preview.SetActive(true);
    }

    private static void CopyTransform(Transform source, Transform target)
    {
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private void CopyPreviewMeshes(Transform source, Transform parent)
    {
        var go = new GameObject(source.name + " (Preview)") { layer = 2, hideFlags = HideFlags.DontSave };
        go.transform.SetParent(parent, false);
        CopyTransform(source, go.transform);
        var filter = source.GetComponent<MeshFilter>();
        if (filter && filter.sharedMesh && source.GetComponent<MeshRenderer>())
        {
            go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var materials = new Material[filter.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = previewMaterial;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        foreach (Transform child in source) CopyPreviewMeshes(child, go.transform);
    }

    private void ClearPreview()
    {
        if (preview)
        {
            preview.SetActive(false);
            if (Application.isPlaying) Destroy(preview);
            else DestroyImmediate(preview);
        }
        preview = null;
        previewSlot = null;
    }

    private void OnDisable()
    {
        highlighted = false;
        ClearPreview();
    }

    private void OnDestroy() => ClearPreview();
}
