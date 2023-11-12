using System;
using UnityEngine;

/// <summary>
/// Example of an implementation of IDamageable
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class DamageableEntity : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHealth;
    
    private int health;
    public int Health //This property calls the Action every time this GameObject takes damage
    {
        get => health;
        set
        {
            health = value;
            OnHealthChanged?.Invoke(health/(float)maxHealth); //I send the percentage of the health left in order to update the health bar
        }
    }

    public Action<float> OnHealthChanged;

    private SpriteRenderer renderer;

    private void Awake()
    {
        renderer = GetComponent<SpriteRenderer>();
    }

    private void Start()
    {
        health = maxHealth;
    }

    /// <summary>
    /// Implementation of IDamageable
    /// </summary>
    public void OnDeath() 
    {
        renderer.color = Color.red;
    }
}

