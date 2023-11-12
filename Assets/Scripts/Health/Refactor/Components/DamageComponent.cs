using UnityEngine;

/// <summary>
/// This is the component that will deal damage to our GameObjects
/// </summary>
public class DamageComponent : MonoBehaviour
{
    [SerializeField] protected int damage;

    protected virtual void DealDamage(IDamageable damageable)
    {
        damageable.TakeDamage(damage);
    }
}

