using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(GroundGrid), typeof(GroundTileRenderer), typeof(GroundTileRendererV2))]
public sealed class GroundTileVersionSwitcher : MonoBehaviour
{
    public enum TileVersion
    {
        V1,
        V2
    }

    [SerializeField] private TileVersion initialVersion = TileVersion.V1;
    [SerializeField] private Key v1TestKey = Key.F1;
    [SerializeField] private Key v2TestKey = Key.F2;

    private GroundTileRenderer v1Renderer;
    private GroundTileRendererV2 v2Renderer;

    public TileVersion ActiveVersion { get; private set; }

    private void Awake()
    {
        v1Renderer = GetComponent<GroundTileRenderer>();
        v2Renderer = GetComponent<GroundTileRendererV2>();
        Apply(initialVersion, false);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard[v1TestKey].wasPressedThisFrame) SetVersion(TileVersion.V1);
        if (keyboard[v2TestKey].wasPressedThisFrame) SetVersion(TileVersion.V2);
    }

    public void SetVersion(TileVersion version)
    {
        Apply(version, true);
    }

    private void Apply(TileVersion version, bool log)
    {
        if (!v1Renderer) v1Renderer = GetComponent<GroundTileRenderer>();
        if (!v2Renderer) v2Renderer = GetComponent<GroundTileRendererV2>();

        // Disable first so the two collision/render chunk sets never overlap.
        v1Renderer.enabled = false;
        v2Renderer.enabled = false;
        if (version == TileVersion.V1) v1Renderer.enabled = true;
        else v2Renderer.enabled = true;

        ActiveVersion = version;
        if (log) Debug.Log($"Tile test version changed to {version}. Dig state is preserved.", this);
    }
}
