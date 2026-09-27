using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class PickupItem : MonoBehaviour, IPlayerHeldItem
{
    [SerializeField, Min(0.05f)] private float mass = 1f;
    [SerializeField] private bool canDrop = true;
    [SerializeField] private bool canThrow = true;

    private Rigidbody body;
    private Collider itemCollider;

    public float Mass => mass;
    public bool CanDrop => canDrop;
    public bool CanThrow => canThrow;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        itemCollider = GetComponent<Collider>();
        body.mass = mass;
    }

    public bool TryPickUp(Transform anchor)
    {
        if (!anchor) return false;
        body.isKinematic = true;
        body.useGravity = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        itemCollider.enabled = false;
        transform.SetParent(anchor, false);
        transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        return true;
    }

    public bool TryDrop(PlayerItemActionContext context)
    {
        if (!CanDrop) return false;
        Release(context.Origin + context.Direction * 0.7f, Vector3.zero);
        return true;
    }

    public bool TryThrow(PlayerItemActionContext context)
    {
        if (!CanThrow) return false;
        Release(context.Origin + context.Direction * 0.7f, context.Direction * context.Speed);
        return true;
    }

    private void Release(Vector3 position, Vector3 velocity)
    {
        transform.SetParent(null, true);
        transform.position = position;
        itemCollider.enabled = true;
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = velocity;
    }
}
