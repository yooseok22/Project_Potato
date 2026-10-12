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

    public float Mass => mass;
    public bool CanDrop => false;
    public bool CanThrow => false;
    public bool IsCarried => currentItems;

    private void Awake() => CaptureHome();

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
        SetColliders(false);
        transform.SetParent(items.HeldItemAnchor, false);
        transform.localPosition = heldLocalPosition;
        transform.localRotation = Quaternion.Euler(heldLocalEulerAngles);
        transform.localScale = heldLocalScale;
        return true;
    }

    public bool TryReturnHome(PlayerInteractionController player)
    {
        if (!IsCarriedBy(player) || !currentItems.TryReleaseFromHands(this)) return false;
        currentItems = null;
        RestoreHomeTransform();
        RestoreColliders();
        return true;
    }

    public bool TryPlace(PlayerInteractionController player, Vector3 position, Quaternion rotation)
    {
        if (!IsCarriedBy(player) || !currentItems.TryReleaseFromHands(this)) return false;
        currentItems = null;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(position, rotation);
        RestoreColliders();
        return true;
    }

    public void ResetHome()
    {
        CaptureHome();
        if (currentItems) currentItems.TryReleaseFromHands(this);
        currentItems = null;
        RestoreHomeTransform();
        RestoreColliders();
    }

    public bool TryDrop(PlayerItemActionContext context) => false;
    public bool TryThrow(PlayerItemActionContext context) => false;

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
