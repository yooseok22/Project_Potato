using UnityEngine;

public sealed class ThrownStunProjectile : MonoBehaviour
{
    public enum ProjectileKind { Potato, Rock, Other }
    [SerializeField] private ProjectileKind kind;
    [SerializeField, Min(0f)] private float forceMultiplier = 1f;
    [SerializeField, Min(0f)] private float potatoBoarDamage = 15f;
    [SerializeField, Min(0f)] private float rockBoarDamage = 25f;
    private Rigidbody body;
    private bool spent;
    private void Awake() => body = GetComponent<Rigidbody>();
    private void OnCollisionEnter(Collision collision) => TryImpact(collision.collider);
    private void OnTriggerEnter(Collider other) => TryImpact(other);

    private void TryImpact(Collider other)
    {
        if (spent || kind == ProjectileKind.Other) return;
        var boar = other.GetComponentInParent<BoarController>();
        if (boar)
        {
            float damage = kind == ProjectileKind.Rock ? rockBoarDamage : potatoBoarDamage;
            if (boar.TryTakeDamage(damage)) spent = true;
            return;
        }
        var target = other.GetComponentInParent<PlayerStunController>();
        if (!target) return;
        spent = true;
        Vector3 away = body && body.linearVelocity.sqrMagnitude > 0.001f
            ? body.linearVelocity.normalized : target.transform.position - transform.position;
        target.Stun(away, kind == ProjectileKind.Potato
            ? PlayerStunCause.ThrownPotato : PlayerStunCause.ThrownRock, forceMultiplier);
    }
}
