/// <summary>Common contract for runtime data that can receive damage.</summary>
public interface IDamageable
{
    float CurrentHealth { get; }
    float MaxHealth { get; }
    bool IsDead { get; }

    /// <summary>Returns true only when positive damage was actually applied.</summary>
    bool TryTakeDamage(float amount);
}
