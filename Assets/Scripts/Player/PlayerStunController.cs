using System.Collections;
using Unity.Netcode;
using UnityEngine;

public enum PlayerStunCause : byte { BoarCharge, ThrownPotato, ThrownRock }

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class PlayerStunController : NetworkBehaviour
{
    [SerializeField, Min(0.1f)] private float stunDuration = 3f;
    [SerializeField, Min(0f)] private float impactVelocity = 6f;
    [SerializeField, Min(0f)] private float upwardVelocity = 2f;
    [Tooltip("Assign humanoid ragdoll bodies when a rig is added. Empty uses the root capsule fallback.")]
    [SerializeField] private Rigidbody[] ragdollBodies;
    [SerializeField] private Collider[] ragdollColliders;
    [SerializeField] private Animator animator;

    private FirstPersonPlayerController movement;
    private PlayerActionBlocker blocker;
    private CharacterController characterController;
    private Rigidbody rootBody;
    private CapsuleCollider rootCollider;
    private Coroutine recovery;

    public bool IsStunned { get; private set; }

    private void Awake()
    {
        movement = GetComponent<FirstPersonPlayerController>();
        blocker = GetComponent<PlayerActionBlocker>();
        characterController = GetComponent<CharacterController>();
        rootBody = GetComponent<Rigidbody>();
        rootCollider = GetComponent<CapsuleCollider>();
        if (!animator) animator = GetComponentInChildren<Animator>();
        ConfigureNormalPhysics();
    }

    public void Stun(Vector3 awayFromAttackDirection, PlayerStunCause cause, float forceMultiplier = 1f)
    {
        Vector3 direction = awayFromAttackDirection.sqrMagnitude > 0.0001f
            ? awayFromAttackDirection.normalized : -transform.forward;
        float force = Mathf.Max(0f, forceMultiplier);
        if (!IsSpawned) BeginStun(direction, force);
        else if (IsServer) ApplyStunClientRpc(direction, force, (byte)cause);
        else RequestStunServerRpc(direction, force, (byte)cause);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestStunServerRpc(Vector3 direction, float force, byte cause)
        => ApplyStunClientRpc(direction, force, cause);

    [ClientRpc]
    private void ApplyStunClientRpc(Vector3 direction, float force, byte cause)
        => BeginStun(direction, force);

    private void BeginStun(Vector3 direction, float multiplier)
    {
        if (recovery != null) StopCoroutine(recovery);
        IsStunned = true;
        blocker.SetBlocked(PlayerActionBlockReason.Stunned, true);
        movement.StopMotion();
        bool simulate = !IsSpawned || IsOwner;
        if (characterController) characterController.enabled = false;
        if (animator) animator.enabled = false;

        bool hasRagdoll = ragdollBodies != null && ragdollBodies.Length > 0;
        if (hasRagdoll) SetChildRagdoll(true, simulate, direction, multiplier);
        else if (simulate)
        {
            rootCollider.enabled = true;
            rootBody.detectCollisions = true;
            rootBody.useGravity = true;
            rootBody.isKinematic = false;
            rootBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rootBody.WakeUp();
            rootBody.AddForce(direction * impactVelocity * multiplier + Vector3.up * upwardVelocity,
                ForceMode.VelocityChange);
        }
        recovery = StartCoroutine(RecoverAfterDelay());
    }

    private IEnumerator RecoverAfterDelay()
    {
        yield return new WaitForSeconds(stunDuration);
        ConfigureNormalPhysics();
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        if (characterController) characterController.enabled = true;
        if (animator) animator.enabled = true;
        blocker.SetBlocked(PlayerActionBlockReason.Stunned, false);
        IsStunned = false;
        recovery = null;
    }

    private void ConfigureNormalPhysics()
    {
        if (!rootBody) rootBody = GetComponent<Rigidbody>();
        if (!rootCollider) rootCollider = GetComponent<CapsuleCollider>();
        rootBody.isKinematic = true;
        rootBody.useGravity = false;
        rootBody.detectCollisions = false;
        rootBody.linearVelocity = Vector3.zero;
        rootBody.angularVelocity = Vector3.zero;
        rootCollider.enabled = false;
        SetChildRagdoll(false, false, Vector3.zero, 0f);
    }

    private void SetChildRagdoll(bool enabled, bool simulate, Vector3 direction, float multiplier)
    {
        if (ragdollColliders != null)
            foreach (var bodyCollider in ragdollColliders)
                if (bodyCollider) bodyCollider.enabled = enabled;
        if (ragdollBodies == null) return;
        foreach (var body in ragdollBodies)
        {
            if (!body || body == rootBody) continue;
            body.isKinematic = !enabled || !simulate;
            body.useGravity = enabled && simulate;
            body.detectCollisions = enabled && simulate;
            if (!enabled || !simulate) continue;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.WakeUp();
            body.AddForce(direction * impactVelocity * multiplier + Vector3.up * upwardVelocity,
                ForceMode.VelocityChange);
        }
    }
}
