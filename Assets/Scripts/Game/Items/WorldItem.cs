using UnityEngine;

/// <summary>
/// Physical world representation of an inventory item. This component stores enough runtime
/// data to recreate the item when the player picks it up and also supports swapping in place.
/// </summary>
public sealed class WorldItem : MonoBehaviour
{
    [Header("Item Data")]
    [Tooltip("Static definition used by this physical world item.")]
    [SerializeField] private ItemDefinition Definition;

    [Tooltip("Current amount stored in this world item.")]
    [SerializeField] private int Amount = 1;

    [Tooltip("Upgrade level stored in this world item.")]
    [SerializeField] private int UpgradeLevel = 0;

    [Tooltip("Durability stored in this world item.")]
    [SerializeField] private float Durability = -1f;

    [Header("Physics")]
    [Tooltip("Optional rigidbody used when this object is dropped or thrown.")]
    [SerializeField] private Rigidbody CachedRigidbody;

    /// <summary>
    /// Initializes cached references.
    /// </summary>
    private void Awake()
    {
        if (CachedRigidbody == null)
        {
            CachedRigidbody = GetComponent<Rigidbody>();
        }

        if (Definition != null && Durability < 0f)
        {
            Durability = Definition.GetDefaultDurability();
        }

        RefreshObjectName();
    }

    /// <summary>
    /// Builds a runtime item instance from the current world state.
    /// </summary>
    public ItemInstance CreateItemInstance()
    {
        if (Definition == null)
        {
            return null;
        }

        float runtimeDurability = Durability < 0f
            ? Definition.GetDefaultDurability()
            : Durability;

        return new ItemInstance(Definition, Amount, UpgradeLevel, runtimeDurability);
    }

    /// <summary>
    /// Applies a runtime item instance to this world representation.
    /// </summary>
    public void ApplyItemInstance(ItemInstance itemInstance)
    {
        if (itemInstance == null)
        {
            return;
        }

        Definition = itemInstance.GetDefinition();
        Amount = itemInstance.GetAmount();
        UpgradeLevel = itemInstance.GetUpgradeLevel();
        Durability = itemInstance.GetDurability();
        RefreshObjectName();
    }

    /// <summary>
    /// Gets the rigidbody attached to this world item, if any.
    /// </summary>
    public Rigidbody GetRigidbody()
    {
        return CachedRigidbody;
    }

    /// <summary>
    /// Gets the current world rotation.
    /// </summary>
    public Quaternion GetWorldRotation()
    {
        return transform.rotation;
    }

    /// <summary>
    /// Gets the current world position.
    /// </summary>
    public Vector3 GetWorldPosition()
    {
        return transform.position;
    }

    /// <summary>
    /// Gets the current rigidbody linear velocity.
    /// </summary>
    public Vector3 GetLinearVelocity()
    {
        if (CachedRigidbody == null)
        {
            return Vector3.zero;
        }

        return CachedRigidbody.linearVelocity;
    }

    /// <summary>
    /// Gets the current rigidbody angular velocity.
    /// </summary>
    public Vector3 GetAngularVelocity()
    {
        if (CachedRigidbody == null)
        {
            return Vector3.zero;
        }

        return CachedRigidbody.angularVelocity;
    }

    /// <summary>
    /// Renames the GameObject for easier debugging in the hierarchy.
    /// </summary>
    private void RefreshObjectName()
    {
        if (Definition == null)
        {
            return;
        }

        gameObject.name = "WorldItem_" + Definition.GetDisplayName();
    }
}
