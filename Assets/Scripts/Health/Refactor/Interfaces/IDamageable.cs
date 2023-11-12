
/// <summary>
/// Implement this interface on the GameObjects you want to take damage
/// </summary>
public interface IDamageable
{
    public int Health { get; set; }

    public void TakeDamage(int damage) //This has mostly to be kept like this, but if you wish you can re-implement it
    {
        Health -= damage;

        if (Health <= 0)
        {
            OnDeath();
        }
    }

    void OnDeath();
}
