using UnityEngine;

[DisallowMultipleComponent]
public sealed class ProcessingCarryItem : MonoBehaviour, IPlayerHeldItem
{
    [SerializeField, Min(0.05f)] private float mass = 1f;
    [SerializeField] private Vector3 heldLocalPosition = new Vector3(0f, 0.15f, 0f);
    [SerializeField] private Vector3 heldLocalEulerAngles;
    [SerializeField] private Vector3 heldLocalScale = Vector3.one;

    private Transform homeParent;
    private Vector3 homeLocalPosition;
    private Quaternion homeLocalRotation;
    private Vector3 homeLocalScale;
    private Collider[] colliders;
    private bool[] colliderStates;
    private PlayerItemController currentItems;
    private bool homeCaptured;
    private ProcessingCarryRig carryRig;
    private Transform handSocket;
    private Transform handAnchor;
    private Transform viewTransform;

    public float Mass => mass;
    public bool CanDrop => false;
    public bool CanThrow => false;
    public bool IsCarried => currentItems;

    private void Awake()
    {
        carryRig = GetComponent<ProcessingCarryRig>();
        CaptureHome();
    }

    public void ConfigureHeldPose(Vector3 position, Vector3 eulerAngles, Vector3 scale)
    {
        heldLocalPosition = position;
        heldLocalEulerAngles = eulerAngles;
        heldLocalScale = scale;
    }

    private void CaptureHome()
    {
        if (homeCaptured) return;
        homeCaptured = true;
        homeParent = transform.parent;
        homeLocalPosition = transform.localPosition;
        homeLocalRotation = transform.localRotation;
        homeLocalScale = transform.localScale;
        colliders = GetComponentsInChildren<Collider>(true);
        colliderStates = new bool[colliders.Length];
        for (int index = 0; index < colliders.Length; index++) colliderStates[index] = colliders[index].enabled;
    }

    public bool IsCarriedBy(PlayerInteractionController player)
        => player && currentItems && currentItems == player.GetComponent<PlayerItemController>();

    public bool TryAttach(PlayerInteractionController player)
    {
        CaptureHome();
        if (!player || currentItems) return false;
        var items = player.GetComponent<PlayerItemController>();
        if (!items || !items.HeldItemAnchor || !items.TryHoldInHands(this)) return false;
        currentItems = items;
        if (!carryRig) carryRig = GetComponent<ProcessingCarryRig>();
        carryRig?.Detach();
        SetColliders(false);
        handAnchor = items.HeldItemAnchor;
        var controller = player.GetComponent<FirstPersonPlayerController>();
        viewTransform = controller && controller.ViewCamera ? controller.ViewCamera.transform : player.transform;
        // The current hand anchor lives below a nonuniformly scaled capsule. A camera socket
        // avoids stretching or shrinking skinned props through that hand's transform scale.
        handSocket = new GameObject(name + "_HandSocket").transform;
        handSocket.SetParent(viewTransform, false);
        UpdateHandSocket();
        transform.SetParent(handSocket, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        Vector3 socketScale = handSocket.lossyScale;
        transform.localScale = new Vector3(heldLocalScale.x / socketScale.x,
            heldLocalScale.y / socketScale.y, heldLocalScale.z / socketScale.z);
        return true;
    }

    public bool TryReturnHome(PlayerInteractionController player)
    {
        if (!IsCarriedBy(player) || !currentItems.TryReleaseFromHands(this)) return false;
        currentItems = null;
        RestoreHomeTransform();
        ClearHandSocket();
        carryRig?.Restore();
        RestoreColliders();
        return true;
    }

    public void ResetHome()
    {
        CaptureHome();
        if (!carryRig) carryRig = GetComponent<ProcessingCarryRig>();
        if (currentItems) currentItems.TryReleaseFromHands(this);
        currentItems = null;
        RestoreHomeTransform();
        ClearHandSocket();
        carryRig?.Restore();
        RestoreColliders();
    }

    public bool TryDrop(PlayerItemActionContext context) => false;

    public bool TryPlaceAt(PlayerInteractionController player, Transform anchor)
    {
        if (!anchor || !IsCarriedBy(player) || !currentItems.TryReleaseFromHands(this)) return false;
        currentItems = null;
        transform.SetParent(anchor, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = homeLocalScale;
        ClearHandSocket();
        gameObject.SetActive(true);
        RestoreColliders();
        return true;
    }
    public bool TryThrow(PlayerItemActionContext context) => false;

    private void LateUpdate()
    {
        if (currentItems && handSocket) UpdateHandSocket();
    }

    private void UpdateHandSocket()
    {
        handSocket.SetPositionAndRotation(handAnchor.position + viewTransform.rotation * heldLocalPosition,
            viewTransform.rotation * Quaternion.Euler(heldLocalEulerAngles));
    }

    private void ClearHandSocket()
    {
        if (handSocket) Destroy(handSocket.gameObject);
        handSocket = handAnchor = viewTransform = null;
    }

    private void RestoreHomeTransform()
    {
        transform.SetParent(homeParent, false);
        transform.localPosition = homeLocalPosition;
        transform.localRotation = homeLocalRotation;
        transform.localScale = homeLocalScale;
        gameObject.SetActive(true);
    }

    private void SetColliders(bool enabled)
    {
        if (colliders == null) return;
        foreach (var itemCollider in colliders) if (itemCollider) itemCollider.enabled = enabled;
    }

    private void RestoreColliders()
    {
        if (colliders == null) return;
        for (int index = 0; index < colliders.Length; index++)
            if (colliders[index]) colliders[index].enabled = colliderStates[index];
    }
}
