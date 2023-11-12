using System;
using UnityEngine;

/// <summary>
/// Example of an implementation of DamageComponent, the bullet wont move, it's only there for example purposes
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class Bullet : DamageComponent
{
    [SerializeField] private float speed = 10f;
    private Rigidbody2D rigidbody2D;
    private BoxCollider2D collider2D;

    private void Awake()
    {
        rigidbody2D = GetComponent<Rigidbody2D>();
        collider2D = GetComponent<BoxCollider2D>();
    }

    private void Start()
    {
        collider2D.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent(out IDamageable damageable))
        {
            DealDamage(damageable);
        }
    }
}

