using UnityEngine;

public sealed class ThrownStunProjectile : MonoBehaviour
{
    public enum ProjectileKind { Potato, Rock, Other }
    [SerializeField] private ProjectileKind kind;
    [SerializeField, Min(0f)] private float forceMultiplier = 1f;
    private Rigidbody body;
    private bool spent;
    private void Awake() => body = GetComponent<Rigidbody>();
    private void OnCollisionEnter(Collision collision)
    {
        if (spent || kind == ProjectileKind.Other) return;
        var target = collision.collider.GetComponentInParent<PlayerStunController>();
        if (!target) return;
        spent = true;
        Vector3 away = body && body.linearVelocity.sqrMagnitude > 0.001f
            ? body.linearVelocity.normalized : target.transform.position - transform.position;
        target.Stun(away, kind == ProjectileKind.Potato
            ? PlayerStunCause.ThrownPotato : PlayerStunCause.ThrownRock, forceMultiplier);
    }
}
