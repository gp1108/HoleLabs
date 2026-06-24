using UnityEngine;

/// <summary>
/// Physical world representation of an inventory item.
/// The inventory identity remains owned by this component, while optional PhysicsCarryable support allows the same object
/// to participate in stable physical systems such as elevator storage without changing hotbar pickup behaviour.
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
    [Tooltip("Optional rigidbody used when this object is dropped, thrown, saved or carried by physical systems.")]
    [SerializeField] private Rigidbody CachedRigidbody;

    [Tooltip("Optional carryable component used by physical systems such as elevator storage. This does not change E pickup behaviour.")]
    [SerializeField] private PhysicsCarryable CachedCarryable;

    [Tooltip("If true, missing physics references are resolved from this object and its parents at runtime.")]
    [SerializeField] private bool AutoResolvePhysicsReferences = true;

    [Tooltip("If true, runtime removal destroys the PhysicsCarryable root when this WorldItem is a child of a carryable root.")]
    [SerializeField] private bool DestroyCarryableRootWhenRuntimeCollected = true;

    /// <summary>
    /// Initializes cached references.
    /// </summary>
    private void Awake()
    {
        ResolvePhysicsReferences();

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
    /// <returns>Item instance matching the current world item data, or null if no definition is assigned.</returns>
    public ItemInstance CreateItemInstance()
    {
        if (Definition == null)
        {
            return null;
        }

        float RuntimeDurability = Durability < 0f
            ? Definition.GetDefaultDurability()
            : Durability;

        return new ItemInstance(Definition, Amount, UpgradeLevel, RuntimeDurability);
    }

    /// <summary>
    /// Applies a runtime item instance to this world representation.
    /// </summary>
    /// <param name="ItemInstance">Item instance to copy into this world item.</param>
    public void ApplyItemInstance(ItemInstance ItemInstance)
    {
        if (ItemInstance == null)
        {
            return;
        }

        Definition = ItemInstance.GetDefinition();
        Amount = ItemInstance.GetAmount();
        UpgradeLevel = ItemInstance.GetUpgradeLevel();
        Durability = ItemInstance.GetDurability();
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
    /// Gets the rigidbody attached to this world item or its carryable root, if any.
    /// </summary>
    /// <returns>Cached rigidbody, or null when the item has no physics body.</returns>
    public Rigidbody GetRigidbody()
    {
        ResolvePhysicsReferences();
        return CachedRigidbody;
    }

    /// <summary>
    /// Gets the carryable component attached to this world item or one of its parents, if any.
    /// </summary>
    /// <returns>Cached carryable, or null when this item is not configured as a physical carryable.</returns>
    public PhysicsCarryable GetCarryable()
    {
        ResolvePhysicsReferences();
        return CachedCarryable;
    }

    /// <summary>
    /// Gets the transform that owns the runtime physics body for this world item.
    /// </summary>
    /// <returns>Physics root transform when a carryable exists, otherwise this world item transform.</returns>
    public Transform GetPhysicsRoot()
    {
        ResolvePhysicsReferences();

        if (CachedCarryable != null)
        {
            return CachedCarryable.transform;
        }

        if (CachedRigidbody != null)
        {
            return CachedRigidbody.transform;
        }

        return transform;
    }

    /// <summary>
    /// Gets the GameObject that should be destroyed when this runtime world item is collected.
    /// Scene-placed items are still handled by ScenePlacedWorldItemPersistence and should not use this as a preservation rule.
    /// </summary>
    /// <returns>Runtime removal root for this world item.</returns>
    public GameObject GetRuntimeRemovalRoot()
    {
        ResolvePhysicsReferences();

        if (DestroyCarryableRootWhenRuntimeCollected && CachedCarryable != null)
        {
            return CachedCarryable.gameObject;
        }

        if (CachedRigidbody != null && CachedRigidbody.GetComponentInChildren<WorldItem>(true) == this)
        {
            return CachedRigidbody.gameObject;
        }

        return gameObject;
    }

    /// <summary>
    /// Gets the current world rotation of the physics root.
    /// </summary>
    /// <returns>World rotation used for save, swap and respawn operations.</returns>
    public Quaternion GetWorldRotation()
    {
        Transform PhysicsRoot = GetPhysicsRoot();
        return PhysicsRoot != null ? PhysicsRoot.rotation : transform.rotation;
    }

    /// <summary>
    /// Gets the current world position of the physics root.
    /// </summary>
    /// <returns>World position used for save, swap and respawn operations.</returns>
    public Vector3 GetWorldPosition()
    {
        Transform PhysicsRoot = GetPhysicsRoot();
        return PhysicsRoot != null ? PhysicsRoot.position : transform.position;
    }

    /// <summary>
    /// Gets the current rigidbody linear velocity.
    /// </summary>
    /// <returns>Current linear velocity, or zero when this item has no rigidbody.</returns>
    public Vector3 GetLinearVelocity()
    {
        Rigidbody RigidbodyComponent = GetRigidbody();
        if (RigidbodyComponent == null)
        {
            return Vector3.zero;
        }

        return RigidbodyComponent.linearVelocity;
    }

    /// <summary>
    /// Gets the current rigidbody angular velocity.
    /// </summary>
    /// <returns>Current angular velocity, or zero when this item has no rigidbody.</returns>
    public Vector3 GetAngularVelocity()
    {
        Rigidbody RigidbodyComponent = GetRigidbody();
        if (RigidbodyComponent == null)
        {
            return Vector3.zero;
        }

        return RigidbodyComponent.angularVelocity;
    }

    /// <summary>
    /// Releases transient physical control before this world item is picked into the hotbar, hidden by scene persistence or destroyed.
    /// This prevents externally carried elevator children, held items or magnetized items from leaving temporary state behind.
    /// </summary>
    public void PrepareForInventoryPickup()
    {
        ResolvePhysicsReferences();

        if (CachedCarryable == null)
        {
            return;
        }

        if (CachedCarryable.GetIsHeld())
        {
            CachedCarryable.EndHold();
        }

        if (CachedCarryable.GetIsMagnetized())
        {
            CachedCarryable.EndMagnet();
        }

        if (CachedCarryable.IsExternallyCarried)
        {
            CachedCarryable.EndExternalCarry(Vector3.zero);
        }
    }

    /// <summary>
    /// Resets physics velocity and wakes the body when this item is returned to the world from inventory or save data.
    /// </summary>
    /// <param name="LinearVelocity">Linear velocity to apply after reset.</param>
    /// <param name="AngularVelocity">Angular velocity to apply after reset.</param>
    /// <param name="WakeUp">If true, wakes the rigidbody after applying velocities.</param>
    public void ApplyPhysicsState(Vector3 LinearVelocity, Vector3 AngularVelocity, bool WakeUp)
    {
        Rigidbody RigidbodyComponent = GetRigidbody();
        if (RigidbodyComponent == null)
        {
            return;
        }

        RigidbodyComponent.linearVelocity = LinearVelocity;
        RigidbodyComponent.angularVelocity = AngularVelocity;

        if (WakeUp)
        {
            RigidbodyComponent.WakeUp();
        }
    }

    /// <summary>
    /// Resets transient physics state and puts the item to sleep for scene persistence restoration.
    /// </summary>
    public void ResetPhysicsForSceneRestore()
    {
        PrepareForInventoryPickup();

        Rigidbody RigidbodyComponent = GetRigidbody();
        if (RigidbodyComponent == null)
        {
            return;
        }

        RigidbodyComponent.linearVelocity = Vector3.zero;
        RigidbodyComponent.angularVelocity = Vector3.zero;
        RigidbodyComponent.Sleep();
    }

    /// <summary>
    /// Resolves optional rigidbody and carryable references from this object and its parents.
    /// </summary>
    private void ResolvePhysicsReferences()
    {
        if (!AutoResolvePhysicsReferences)
        {
            return;
        }

        if (CachedCarryable == null)
        {
            CachedCarryable = GetComponent<PhysicsCarryable>();

            if (CachedCarryable == null)
            {
                CachedCarryable = GetComponentInParent<PhysicsCarryable>();
            }
        }

        if (CachedRigidbody == null)
        {
            if (CachedCarryable != null)
            {
                CachedRigidbody = CachedCarryable.Rigidbody;
            }

            if (CachedRigidbody == null)
            {
                CachedRigidbody = GetComponent<Rigidbody>();

                if (CachedRigidbody == null)
                {
                    CachedRigidbody = GetComponentInParent<Rigidbody>();
                }
            }
        }
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
