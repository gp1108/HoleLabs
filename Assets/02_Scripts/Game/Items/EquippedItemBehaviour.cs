using UnityEngine;

/// <summary>
/// Base behaviour for any equipped item. Inherit from this for tools such as pickaxes,
/// scanners or weapons. If an item has no equipped prefab, the hotbar can still store it.
/// This version includes interruption hooks so item switching stays safe even if
/// the current equipped item is playing animations, VFX or timed actions.
/// </summary>
public abstract class EquippedItemBehaviour : MonoBehaviour
{
    [Tooltip("Current runtime item instance associated with this equipped behaviour.")]
    protected ItemInstance ItemInstance;

    [Tooltip("Hotbar controller that owns this equipped item.")]
    protected HotbarController OwnerHotbar;

    [Tooltip("Whether the item is currently using its primary action.")]
    protected bool IsPrimaryUseActive;

    [Tooltip("Whether the item is currently using its secondary action.")]
    protected bool IsSecondaryUseActive;

    /// <summary>
    /// Initializes the equipped item with its runtime data and owner.
    /// </summary>
    public virtual void Initialize(HotbarController ownerHotbar, ItemInstance itemInstance)
    {
        OwnerHotbar = ownerHotbar;
        ItemInstance = itemInstance;
    }

    /// <summary>
    /// Called once after the item becomes the selected hotbar entry.
    /// </summary>
    public virtual void OnEquipped()
    {
    }

    /// <summary>
    /// Called by the hotbar before the item is unequipped or replaced.
    /// Use this to stop animations, sounds, coroutines, charge states or VFX safely.
    /// </summary>
    public virtual void ForceStopItemUsage()
    {
        IsPrimaryUseActive = false;
        IsSecondaryUseActive = false;
    }

    /// <summary>
    /// Called once before the item is removed as the selected hotbar entry.
    /// </summary>
    public virtual void OnUnequipped()
    {
    }

    /// <summary>
    /// Called when the primary use input is pressed.
    /// </summary>
    public virtual void OnPrimaryUseStarted()
    {
        IsPrimaryUseActive = true;
    }

    /// <summary>
    /// Called every frame while the primary use input is held.
    /// </summary>
    public virtual void OnPrimaryUseHeld()
    {
    }

    /// <summary>
    /// Called when the primary use input is released.
    /// </summary>
    public virtual void OnPrimaryUseEnded()
    {
        IsPrimaryUseActive = false;
    }

    /// <summary>
    /// Called when the secondary use input is pressed.
    /// </summary>
    public virtual void OnSecondaryUseStarted()
    {
        IsSecondaryUseActive = true;
    }

    /// <summary>
    /// Called every frame while the secondary use input is held.
    /// </summary>
    public virtual void OnSecondaryUseHeld()
    {
    }

    /// <summary>
    /// Called when the secondary use input is released.
    /// </summary>
    public virtual void OnSecondaryUseEnded()
    {
        IsSecondaryUseActive = false;
    }
}
