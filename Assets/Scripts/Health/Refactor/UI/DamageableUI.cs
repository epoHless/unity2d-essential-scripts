using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI Representation of a DamageableEntity health condition 
/// </summary>
[RequireComponent(typeof(Image))]
public class DamageableUI : MonoBehaviour
{
    [SerializeField] private DamageableEntity damageableEntity; //the associated entity
    private Image healthBar;

    private void Awake()
    {
        healthBar = GetComponent<Image>();
    }

    private void OnEnable()
    {
        damageableEntity.OnHealthChanged += SetHealthBar;
    }

    private void OnDisable()
    {
        damageableEntity.OnHealthChanged -= SetHealthBar;
    }

    private void SetHealthBar(float value) //This function is called every time the entity takes damage
    {
        healthBar.fillAmount = value;
    }
}

