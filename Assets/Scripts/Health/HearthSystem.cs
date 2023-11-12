using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Pixeye.Unity;

[Obsolete]
public class HearthSystem : MonoBehaviour
{
    [SerializeField] int currentHealth;

    [SerializeField] int maxHearts; 

    [Header("Total possible hearths")]
    [SerializeField] Image[] hearts;

    [Foldout("Health Sprites", true)]
    [SerializeField] Sprite fullHeart;
    [SerializeField] Sprite emptyHeart;

    private void Start()
    {
        maxHearts = hearts.Length; //By default maxHearts is equal to the array of images, but you can change this to be less if you want to give the player the option to gain more hearths as the game progresses

        //currentHealth = maxHearts; //use this if you want your player to have it's health set to the Max on every load
        currentHealth = PlayerPrefs.GetInt("Health"); //use this if you want to save the value on set it if you reload the scene
    }

    private void Update()
    {

        if (currentHealth > maxHearts)
        {
            currentHealth = maxHearts;
        }

        for (int i = 0; i < hearts.Length; i++)
        {
            if (i < currentHealth)
            {
                hearts[i].sprite = fullHeart;
            }
            else
            {
                hearts[i].sprite = emptyHeart;
            }

            if (i < maxHearts)
            {
                hearts[i].enabled = true;
            }
            else
            {
                hearts[i].enabled = false;
            }
        }

        if(currentHealth <= 0)
        {
            currentHealth = 0;
        }if(currentHealth > maxHearts)
        {
            currentHealth = maxHearts;
        }

        PlayerPrefs.SetInt("Health", currentHealth);

        #region This is for testing porpuses only, remove if unnecessary

        if (Input.GetKeyDown(KeyCode.X))
        {
            RemoveHealth(1);
        }else if (Input.GetKeyDown(KeyCode.C))
        {
            AddHealth(1);
        }

        #endregion
    }

    public void AddHealth(int healthToAdd)
    {
        currentHealth += healthToAdd;
    }

    public void RemoveHealth(int healthToRemove)
    {
        currentHealth -= healthToRemove;
    }

    public void AddHearth(int hearthToAdd)
    {
        maxHearts += hearthToAdd;
    }

    public void RemoveHearth(int hearthToRemove)
    {
        maxHearts -= hearthToRemove;
    }
}
