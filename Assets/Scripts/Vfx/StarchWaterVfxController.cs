using UnityEngine;

/// <summary>Wet contents in the red basket drip until draining finishes, including partial fills.</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PotatoStarchProcessor))]
public sealed class StarchWaterVfxController : MonoBehaviour
{
    [SerializeField] private PotatoStarchProcessor processor;
    [SerializeField] private GameObject dropPrefab;
    [SerializeField] private Transform emissionAnchor;
    [Tooltip("Compensates for the source emitter's authored height inside the VFX prefab.")]
    [SerializeField] private Vector3 effectOffset = new(0f, -1.09f, 0f);
    private ParticleVfxInstance effect;

    public bool IsEmitting => effect && effect.gameObject.activeSelf;

    public void Configure(PotatoStarchProcessor value, GameObject prefab, Transform anchor)
    {
        processor = value;
        dropPrefab = prefab;
        emissionAnchor = anchor;
    }

    private void Awake()
    {
        if (!processor) processor = GetComponent<PotatoStarchProcessor>();
    }

    private void LateUpdate() => RefreshVfx();

    public void RefreshVfx()
    {
        bool wet = processor && processor.isActiveAndEnabled && emissionAnchor && processor.SieveFillCount > 0 &&
            (processor.SieveState == StarchSieveState.Filling ||
             processor.SieveState == StarchSieveState.FillingSegment ||
             processor.SieveState == StarchSieveState.Draining);
        if (!wet) { Clear(); return; }
        Vector3 position = emissionAnchor.position + effectOffset;
        if (!effect) effect = ParticleVfxInstance.Spawn(dropPrefab, position, transform, true);
        if (effect) effect.transform.position = position;
    }

    private void Clear()
    {
        if (effect) effect.Release();
        effect = null;
    }

    private void OnDisable() => Clear();
}
