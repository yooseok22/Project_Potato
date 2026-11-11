using UnityEngine;

public sealed class PlayerVisualController : MonoBehaviour
{
    public bool MotionEffectsEnabled { get; set; } = true;
    [SerializeField] private Renderer worldRenderer;
    [SerializeField] private Color handColor = new Color(0.92f, 0.72f, 0.55f, 1f);

    private Transform viewRoot;
    private Transform leftHand;
    private Transform rightHand;
    private Renderer leftHandRenderer;
    private Renderer rightHandRenderer;
    private Vector3 leftRest;
    private Vector3 rightRest;
    private Material handMaterial;

    public Transform HeldItemAnchor { get; private set; }

    private void Awake()
    {
        if (!worldRenderer) worldRenderer = GetComponent<Renderer>();
    }

    public void Configure(Camera camera, bool localPlayer)
    {
        if (worldRenderer) worldRenderer.enabled = !localPlayer;
        if (!localPlayer || !camera || viewRoot) return;
        viewRoot = new GameObject("FirstPersonHands").transform;
        viewRoot.SetParent(camera.transform, false);
        leftRest = new Vector3(-0.24f, -0.24f, 0.58f);
        rightRest = new Vector3(0.24f, -0.24f, 0.58f);
        leftHand = CreateHand("LeftHand", leftRest, new Vector3(62f, 0f, -8f));
        rightHand = CreateHand("RightHand", rightRest, new Vector3(62f, 0f, 8f));
        leftHandRenderer = leftHand.GetComponent<Renderer>();
        rightHandRenderer = rightHand.GetComponent<Renderer>();
        HeldItemAnchor = new GameObject("HeldItemAnchor").transform;
        HeldItemAnchor.SetParent(rightHand, false);
        HeldItemAnchor.localPosition = new Vector3(0f, 0.26f, 0f);
        HeldItemAnchor.localRotation = Quaternion.identity;
    }

    public void SetHandsVisible(bool visible)
    {
        if (leftHandRenderer) leftHandRenderer.enabled = visible;
        if (rightHandRenderer) rightHandRenderer.enabled = visible;
    }

    public void SetPose(bool jumping, bool crouching, float movement)
    {
        if (!viewRoot) return;
        float bob = jumping || !MotionEffectsEnabled ? 0f : Mathf.Sin(Time.time * Mathf.Lerp(0f, 10f, movement)) * 0.008f * movement;
        Vector3 spread = jumping ? new Vector3(0.09f, 0.03f, -0.04f) : Vector3.zero;
        leftHand.localPosition = leftRest + new Vector3(-spread.x, spread.y + bob, spread.z);
        rightHand.localPosition = rightRest + new Vector3(spread.x, spread.y + bob, spread.z);
        leftHand.localRotation = Quaternion.Euler(62f, 0f, jumping ? -24f : -8f);
        rightHand.localRotation = Quaternion.Euler(62f, 0f, jumping ? 24f : 8f);
        viewRoot.localPosition = crouching ? Vector3.down * 0.03f : Vector3.zero;
    }

    private Transform CreateHand(string name, Vector3 position, Vector3 rotation)
    {
        var hand = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        hand.name = name;
        hand.transform.SetParent(viewRoot, false);
        hand.transform.localPosition = position;
        hand.transform.localEulerAngles = rotation;
        hand.transform.localScale = new Vector3(0.09f, 0.18f, 0.09f);
        var collider = hand.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        if (!handMaterial)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            handMaterial = new Material(shader) { name = "First Person Hands (Runtime)", color = handColor };
        }
        hand.GetComponent<Renderer>().sharedMaterial = handMaterial;
        return hand.transform;
    }

    private void OnDestroy()
    {
        if (handMaterial) Destroy(handMaterial);
    }
}
