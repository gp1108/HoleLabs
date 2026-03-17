using UnityEngine;

[CreateAssetMenu(fileName = "ItemDefinition_", menuName = "Game/Items/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Unique identifier used to distinguish this item definition from others.")]
    [SerializeField] private string ItemId;

    [Tooltip("Display name shown in UI and debug tools.")]
    [SerializeField] private string DisplayName;

    [Tooltip("Optional icon displayed in the hotbar or inventory UI.")]
    [SerializeField] private Sprite Icon;

    [Header("Prefabs")]
    [Tooltip("Physical prefab spawned in the world when this item is dropped.")]
    [SerializeField] private GameObject WorldPrefab;

    [Tooltip("Optional prefab instantiated while the item is equipped. Leave empty for logic only items.")]
    [SerializeField] private GameObject EquippedPrefab;

    [Header("Stacking")]
    [Tooltip("Whether this item can stack with another item instance of the same definition.")]
    [SerializeField] private bool IsStackable = false;

    [Tooltip("Maximum amount allowed in a single stack.")]
    [SerializeField] private int MaxStackSize = 1;

    [Header("Runtime Defaults")]
    [Tooltip("Default durability assigned when a new runtime instance is created.")]
    [SerializeField] private float DefaultDurability = 100f;

    [Tooltip("If true, the item will be auto-equipped when picked into an empty selected slot.")]
    [SerializeField] private bool AutoEquipWhenSelected = true;

    /// <summary>
    /// Gets the unique identifier of this item definition.
    /// </summary>
    public string GetItemId()
    {
        return ItemId;
    }

    /// <summary>
    /// Gets the display name of this item definition.
    /// </summary>
    public string GetDisplayName()
    {
        return DisplayName;
    }

    /// <summary>
    /// Gets the icon used by UI for this item definition.
    /// </summary>
    public Sprite GetIcon()
    {
        return Icon;
    }

    /// <summary>
    /// Gets the physical world prefab used when dropping the item.
    /// </summary>
    public GameObject GetWorldPrefab()
    {
        return WorldPrefab;
    }

    /// <summary>
    /// Gets the equipped prefab used while the item is selected.
    /// </summary>
    public GameObject GetEquippedPrefab()
    {
        return EquippedPrefab;
    }

    /// <summary>
    /// Gets whether this item supports stacking.
    /// </summary>
    public bool GetIsStackable()
    {
        return IsStackable;
    }

    /// <summary>
    /// Gets the maximum stack size allowed for this item.
    /// </summary>
    public int GetMaxStackSize()
    {
        return Mathf.Max(1, MaxStackSize);
    }

    /// <summary>
    /// Gets the default durability value assigned on creation.
    /// </summary>
    public float GetDefaultDurability()
    {
        return DefaultDurability;
    }

    /// <summary>
    /// Gets whether the item should auto-equip when placed into the selected slot.
    /// </summary>
    public bool GetAutoEquipWhenSelected()
    {
        return AutoEquipWhenSelected;
    }

    /// <summary>
    /// Creates a fresh runtime instance using this static definition.
    /// </summary>
    public ItemInstance CreateRuntimeInstance(int amount = 1)
    {
        return new ItemInstance(this, amount, 0, DefaultDurability);
    }
}
