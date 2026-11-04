using UnityEngine;

public sealed class BoarChargeHitbox : MonoBehaviour
{
    [SerializeField, Min(0f)] private float forceMultiplier = 1f;
    [SerializeField, Min(0f)] private float dogDamage = 10f;
    private DogHelperController hitDog;
    public bool IsCharging { get; private set; }
    public void SetCharging(bool value)
    {
        IsCharging = value;
        if (value) hitDog = null;
    }
    private void OnCollisionEnter(Collision collision) => TryStun(collision.collider, collision.GetContact(0).point);
    private void OnTriggerEnter(Collider other) => TryStun(other, transform.position);
    private void TryStun(Collider other, Vector3 contactPoint)
    {
        if (!IsCharging) return;
        var dog = other.GetComponentInParent<DogHelperController>();
        if (dog)
        {
            if (hitDog == dog) return;
            hitDog = dog;
            if (dog.TryTakeDamage(dogDamage) && !dog.IsDead) dog.TryStun();
            return;
        }
        var target = other.GetComponentInParent<PlayerStunController>();
        if (!target) return;
        Vector3 away = target.transform.position - contactPoint;
        away.y = 0f;
        target.Stun(away, PlayerStunCause.BoarCharge, forceMultiplier);
    }
}
