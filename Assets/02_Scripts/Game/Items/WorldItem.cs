using UnityEngine;

/// <summary>
/// Physical world representation of an inventory item. This component stores enough runtime
/// data to recreate the item when the player picks it up and also supports swapping in place.
/// </summary>
public sealed class WorldItem : MonoBehaviour, IWeightProvider
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
    /// Registers this active world item in the runtime object registry.
    /// </summary>
    private void OnEnable()
    {
        RuntimeWorldObjectRegistry.RegisterWorldItem(this);
    }

    /// <summary>
    /// Removes this world item from the runtime object registry when it becomes inactive.
    /// </summary>
    private void OnDisable()
    {
        RuntimeWorldObjectRegistry.UnregisterWorldItem(this);
    }

    /// <summary>
    /// Removes this world item from the runtime object registry when it is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        RuntimeWorldObjectRegistry.UnregisterWorldItem(this);
    }


    /// <summary>
    /// Gets the static definition currently assigned to this world item.
    /// Save and registry systems use this to resolve derived item definitions such as pickaxes.
    /// </summary>
    /// <returns>Assigned item definition, or null when this world item is not configured.</returns>
    public ItemDefinition GetDefinition()
    {
        return Definition;
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
    /// Gets the gameplay weight contributed by this world item.
    /// Stack amount multiplies the item definition base weight.
    /// </summary>
    /// <returns>Non-negative gameplay weight.</returns>
    public float GetWeight()
    {
        if (Definition == null)
        {
            return 0f;
        }

        return Mathf.Max(0f, Definition.GetBaseWeight()) * Mathf.Max(1, Amount);
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
