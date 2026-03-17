using System;
using UnityEngine;

[Serializable]
public sealed class ItemInstance
{
    [Tooltip("Static item definition that describes this runtime instance.")]
    [SerializeField] private ItemDefinition Definition;

    [Tooltip("Current amount stored in this runtime instance.")]
    [SerializeField] private int Amount;

    [Tooltip("Upgrade level applied to this item instance.")]
    [SerializeField] private int UpgradeLevel;

    [Tooltip("Current durability value for this item instance.")]
    [SerializeField] private float Durability;

    /// <summary>
    /// Initializes a new runtime item instance.
    /// </summary>
    public ItemInstance(ItemDefinition definition, int amount, int upgradeLevel, float durability)
    {
        Definition = definition;
        Amount = Mathf.Max(1, amount);
        UpgradeLevel = Mathf.Max(0, upgradeLevel);
        Durability = durability;
    }

    /// <summary>
    /// Gets the static definition attached to this runtime item.
    /// </summary>
    public ItemDefinition GetDefinition()
    {
        return Definition;
    }

    /// <summary>
    /// Gets the current amount stored in this runtime item.
    /// </summary>
    public int GetAmount()
    {
        return Amount;
    }

    /// <summary>
    /// Sets the current amount stored in this runtime item.
    /// </summary>
    public void SetAmount(int amount)
    {
        Amount = Mathf.Max(0, amount);
    }

    /// <summary>
    /// Gets the current upgrade level of this item.
    /// </summary>
    public int GetUpgradeLevel()
    {
        return UpgradeLevel;
    }

    /// <summary>
    /// Sets the current upgrade level of this item.
    /// </summary>
    public void SetUpgradeLevel(int upgradeLevel)
    {
        UpgradeLevel = Mathf.Max(0, upgradeLevel);
    }

    /// <summary>
    /// Gets the current durability of this item.
    /// </summary>
    public float GetDurability()
    {
        return Durability;
    }

    /// <summary>
    /// Sets the current durability of this item.
    /// </summary>
    public void SetDurability(float durability)
    {
        Durability = durability;
    }

    /// <summary>
    /// Creates a copy of this runtime item instance.
    /// </summary>
    public ItemInstance Clone()
    {
        return new ItemInstance(Definition, Amount, UpgradeLevel, Durability);
    }

    /// <summary>
    /// Checks whether this runtime instance can stack with another one.
    /// </summary>
    public bool CanStackWith(ItemInstance otherItem)
    {
        if (otherItem == null)
        {
            return false;
        }

        if (Definition == null || otherItem.Definition == null)
        {
            return false;
        }

        if (!Definition.GetIsStackable() || !otherItem.Definition.GetIsStackable())
        {
            return false;
        }

        return Definition == otherItem.Definition &&
               UpgradeLevel == otherItem.UpgradeLevel &&
               Mathf.Approximately(Durability, otherItem.Durability);
    }
}
