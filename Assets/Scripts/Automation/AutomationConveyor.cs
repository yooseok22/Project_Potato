using UnityEngine;

/// <summary>Only bodies touching the belt's top receive transport velocity; congestion stays physical.</summary>
[RequireComponent(typeof(AutomationBlock))]
public sealed class AutomationConveyor : MonoBehaviour
{
    private AutomationBlock block;
    private readonly Collider[] restingBodies = new Collider[32];
    private void Awake() => block = GetComponent<AutomationBlock>();

    private void FixedUpdate()
    {
        if (!block || block.TransportSpeed <= 0 || !AutomationWorld.Offline) return;
        int count = Physics.OverlapBoxNonAlloc(transform.position + Vector3.up * 0.30f,
            new Vector3(block.BlockSize * 0.49f, 0.1f, block.BlockSize * 0.49f), restingBodies,
            transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            var body = restingBodies[index].attachedRigidbody;
            if (body && !body.isKinematic) body.WakeUp();
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!block || block.TransportSpeed <= 0f || !AutomationWorld.Offline) return;
        var body = collision.rigidbody;
        if (!body || body.isKinematic) return;
        bool onTop = false;
        for (int index = 0; index < collision.contactCount; index++)
        {
            var contact = collision.GetContact(index);
            Vector3 local = transform.InverseTransformPoint(contact.point);
            if (local.y >= 0.20f && Mathf.Abs(contact.normal.y) > 0.5f) { onTop = true; break; }
        }
        if (!onTop) return;
        Vector3 center = transform.InverseTransformPoint(body.worldCenterOfMass);
        if (Mathf.Abs(center.x) > block.BlockSize * 0.5f || Mathf.Abs(center.z) > block.BlockSize * 0.5f) return;
        Vector3 desired = transform.forward * block.TransportSpeed;
        Vector3 horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        body.AddForce(Vector3.ClampMagnitude(desired - horizontal, 12f * Time.fixedDeltaTime), ForceMode.VelocityChange);
        body.WakeUp();
    }
}
