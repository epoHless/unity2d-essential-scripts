using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Pixeye.Unity;

[RequireComponent(typeof(Slider)), Obsolete]
public class HealthBar : MonoBehaviour
{
    [Foldout("Health Setting", true)]
    [SerializeField] int health;
    [SerializeField] int maxHealth = 100;
    [Range(0,0.05f)] [SerializeField] private float speedOfHealth;
    [SerializeField] private string maxHpPlayerPrefName;
    [SerializeField] bool gameOver;

    [Foldout("Keys Settings", true)]
    [SerializeField] KeyCode healKey = KeyCode.C;
    [SerializeField] KeyCode increaseHealthKey = KeyCode.D;
    [SerializeField] KeyCode takeDamageKey = KeyCode.X;

    Slider healthBar;

    public static HealthBar instance;

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        healthBar = GetComponent<Slider>();

        if (PlayerPrefs.HasKey(maxHpPlayerPrefName))
        {
            maxHealth = (int)PlayerPrefs.GetFloat(maxHpPlayerPrefName);
            healthBar.maxValue = maxHealth;
        }
        else
        {
            healthBar.maxValue = maxHealth;
        }

        health = maxHealth;
    }

    void Update()
    {
        healthBar.value = health;

        if (Input.GetKeyDown(healKey))
        {
            Heal(10);
        }
        else if (Input.GetKeyDown(takeDamageKey))
        {
            TakeDamage(10);
        }else if (Input.GetKeyDown(KeyCode.K))
        {
            IncreaseMaxHp(10);
        }

        if(health <= 0)
        {
            health = 0;
            gameOver = true;
        }else if(health > maxHealth)
        {
            health = maxHealth;
        }
    }

    public void TakeDamage(int damage)
    {
        StartCoroutine(TakeDamageCoroutine(damage));
    }

    public void Heal(int heal)
    {
        StartCoroutine(HealCoroutine(heal));
    }

    public void IncreaseMaxHp(int hpToAdd)
    {
        maxHealth += hpToAdd;
        healthBar.maxValue = maxHealth;
        PlayerPrefs.SetFloat(maxHpPlayerPrefName, maxHealth);
    }

    private IEnumerator TakeDamageCoroutine(int damage)
    {
        for (int i = 0; i < damage; i++)
        {
            health -= 1;
            yield return new WaitForSeconds(speedOfHealth);
        }
    }
    
    private IEnumerator HealCoroutine(int hpToAdd)
    {
        for (int i = 0; i < hpToAdd; i++)
        {
            health += 1;
            yield return new WaitForSeconds(speedOfHealth);
        }
    }
}