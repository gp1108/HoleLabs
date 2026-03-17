using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles runtime hotbar storage, selection, equipping, dropping and swapping.
/// This version exposes UI notifications so the hotbar can update without polling heavy logic.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public sealed class HotbarController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to calculate drop direction and helper defaults.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Socket used to parent equipped prefabs while selected.")]
    [SerializeField] private Transform EquippedItemSocket;

    [Header("Hotbar")]
    [Tooltip("Current number of slots available in the hotbar.")]
    [SerializeField] private int SlotCount = 8;

    [Tooltip("If true, the controller equips the currently selected slot on start.")]
    [SerializeField] private bool EquipSelectedSlotOnStart = true;

    [Header("Input Actions")]
    [Tooltip("Input action used to change hotbar selection with the mouse wheel or equivalent.")]
    [SerializeField] private string HotbarScrollActionName = "HotbarScroll";

    [Tooltip("Input action used for the primary item action.")]
    [SerializeField] private string UsePrimaryActionName = "UsePrimary";

    [Tooltip("Input action used for the secondary item action.")]
    [SerializeField] private string UseSecondaryActionName = "UseSecondary";

    [Tooltip("Input action used to drop the selected hotbar item into the world.")]
    [SerializeField] private string DropItemActionName = "DropItem";

    [Tooltip("Prefix used for direct slot selection input actions such as Slot1, Slot2 and so on.")]
    [SerializeField] private string SlotActionPrefix = "Slot";

    [Header("Drop")]
    [Tooltip("Forward distance used when spawning a dropped world item.")]
    [SerializeField] private float DropForwardDistance = 1.5f;

    [Tooltip("Vertical offset applied when spawning a dropped world item.")]
    [SerializeField] private float DropVerticalOffset = 0.15f;

    [Tooltip("Impulse applied to the dropped world item rigidbody.")]
    [SerializeField] private float DropImpulse = 2f;

    [Header("Debug")]
    [Tooltip("Logs hotbar operations to the console.")]
    [SerializeField] private bool DebugLogs = false;

    [Tooltip("Draws the drop spawn ray in the Scene view.")]
    [SerializeField] private bool DrawDropDebug = false;

    [Tooltip("Runtime item instances stored in each hotbar slot.")]
    [SerializeField] private List<ItemInstance> Slots = new List<ItemInstance>();

    [Tooltip("Current selected hotbar slot index.")]
    [SerializeField] private int SelectedIndex = 0;

    private PlayerInput PlayerInput;
    private InputAction HotbarScrollAction;
    private InputAction UsePrimaryAction;
    private InputAction UseSecondaryAction;
    private InputAction DropItemAction;

    private readonly List<InputAction> SlotActions = new List<InputAction>();

    private GameObject CurrentEquippedObject;
    private EquippedItemBehaviour CurrentEquippedBehaviour;

    /// <summary>
    /// Fired when any slot content changes. The integer argument is the changed slot index.
    /// </summary>
    public event Action<int> OnSlotChanged;

    /// <summary>
    /// Fired when the selected slot changes. The integer argument is the new selected slot index.
    /// </summary>
    public event Action<int> OnSelectedSlotChanged;

    /// <summary>
    /// Fired when the hotbar slot count changes. Useful when rebuilding UI.
    /// </summary>
    public event Action OnHotbarStructureChanged;

    /// <summary>
    /// Gets the current selected hotbar slot index.
    /// </summary>
    public int GetSelectedIndex()
    {
        return SelectedIndex;
    }

    /// <summary>
    /// Gets the total amount of slots currently available in the hotbar.
    /// </summary>
    public int GetSlotCount()
    {
        return SlotCount;
    }

    /// <summary>
    /// Gets the runtime item stored in the provided slot index.
    /// </summary>
    public ItemInstance GetItemAtSlot(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            return null;
        }

        return Slots[slotIndex];
    }

    /// <summary>
    /// Initializes references and validates hotbar data.
    /// </summary>
    private void Awake()
    {
        PlayerInput = GetComponent<PlayerInput>();

        if (PlayerCamera == null)
        {
            PlayerCamera = Camera.main;
        }

        EnsureSlotListSize(SlotCount);
        BindActions();

        if (EquipSelectedSlotOnStart)
        {
            EquipSelectedItem();
        }
    }

    /// <summary>
    /// Processes hotbar input and forwards item use events.
    /// </summary>
    private void Update()
    {
        HandleSelectionInput();
        HandleUseInput();
        HandleDropInput();
    }

    /// <summary>
    /// Resizes the hotbar while preserving existing slot content when possible.
    /// </summary>
    public void ResizeHotbar(int newSlotCount)
    {
        SlotCount = Mathf.Max(1, newSlotCount);
        EnsureSlotListSize(SlotCount);
        RebuildSlotActions();

        if (SelectedIndex >= SlotCount)
        {
            SelectedIndex = SlotCount - 1;
            RefreshEquippedItem();
            OnSelectedSlotChanged?.Invoke(SelectedIndex);
        }

        OnHotbarStructureChanged?.Invoke();
    }

    /// <summary>
    /// Checks whether the currently selected slot contains an item.
    /// </summary>
    public bool HasSelectedItem()
    {
        return GetSelectedItem() != null;
    }

    /// <summary>
    /// Gets the runtime item stored in the currently selected slot.
    /// </summary>
    public ItemInstance GetSelectedItem()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Slots.Count)
        {
            return null;
        }

        return Slots[SelectedIndex];
    }

    /// <summary>
    /// Attempts to add an item into the hotbar.
    /// Preferred slot is tried first, then compatible stacks, then empty slots.
    /// </summary>
    public bool TryAddItem(ItemInstance itemInstance, int preferredSlotIndex, out int insertedSlotIndex)
    {
        insertedSlotIndex = -1;

        if (itemInstance == null || itemInstance.GetDefinition() == null)
        {
            return false;
        }

        EnsureSlotListSize(SlotCount);

        if (TryStackIntoSlot(itemInstance, preferredSlotIndex))
        {
            insertedSlotIndex = preferredSlotIndex;
            NotifySlotChanged(insertedSlotIndex);
            return true;
        }

        for (int index = 0; index < Slots.Count; index++)
        {
            if (index == preferredSlotIndex)
            {
                continue;
            }

            if (TryStackIntoSlot(itemInstance, index))
            {
                insertedSlotIndex = index;
                NotifySlotChanged(insertedSlotIndex);
                return true;
            }
        }

        if (IsValidSlotIndex(preferredSlotIndex) && Slots[preferredSlotIndex] == null)
        {
            Slots[preferredSlotIndex] = itemInstance.Clone();
            insertedSlotIndex = preferredSlotIndex;
            AutoEquipIfNeeded(insertedSlotIndex);
            NotifySlotChanged(insertedSlotIndex);
            Log("Added item to preferred empty slot: " + insertedSlotIndex);
            return true;
        }

        for (int index = 0; index < Slots.Count; index++)
        {
            if (Slots[index] != null)
            {
                continue;
            }

            Slots[index] = itemInstance.Clone();
            insertedSlotIndex = index;
            AutoEquipIfNeeded(insertedSlotIndex);
            NotifySlotChanged(insertedSlotIndex);
            Log("Added item to first free slot: " + insertedSlotIndex);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Replaces the item in the selected slot and returns the previous one.
    /// </summary>
    public ItemInstance ReplaceSelectedItem(ItemInstance newItemInstance)
    {
        if (newItemInstance == null)
        {
            return null;
        }

        EnsureSlotListSize(SlotCount);

        ItemInstance previousItem = Slots[SelectedIndex];
        Slots[SelectedIndex] = newItemInstance.Clone();
        RefreshEquippedItem();
        NotifySlotChanged(SelectedIndex);

        Log("Replaced selected slot item at index: " + SelectedIndex);
        return previousItem;
    }

    /// <summary>
    /// Removes and returns the item currently stored in the selected slot.
    /// </summary>
    public ItemInstance RemoveSelectedItem()
    {
        EnsureSlotListSize(SlotCount);

        ItemInstance removedItem = Slots[SelectedIndex];
        Slots[SelectedIndex] = null;
        RefreshEquippedItem();
        NotifySlotChanged(SelectedIndex);

        Log("Removed selected item at index: " + SelectedIndex);
        return removedItem;
    }

    /// <summary>
    /// Drops the currently selected item into the world and clears the slot.
    /// </summary>
    public bool DropSelectedItemToWorld()
    {
        ItemInstance itemToDrop = RemoveSelectedItem();

        if (itemToDrop == null)
        {
            return false;
        }

        return SpawnWorldItem(itemToDrop, GetDropSpawnPosition(), Quaternion.LookRotation(GetDropDirection(), Vector3.up), Vector3.zero, Vector3.zero, true);
    }

    /// <summary>
    /// Spawns a physical world item from a runtime instance using a forward impulse.
    /// </summary>
    public bool SpawnWorldItem(ItemInstance itemInstance, Vector3 position, Vector3 direction)
    {
        Quaternion rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.0001f ? direction : transform.forward, Vector3.up);
        return SpawnWorldItem(itemInstance, position, rotation, Vector3.zero, Vector3.zero, true);
    }

    /// <summary>
    /// Spawns a physical world item from a runtime instance using explicit transform and physics values.
    /// </summary>
    public bool SpawnWorldItem(ItemInstance itemInstance, Vector3 position, Quaternion rotation, Vector3 linearVelocity, Vector3 angularVelocity, bool applyDropImpulse)
    {
        if (itemInstance == null || itemInstance.GetDefinition() == null)
        {
            return false;
        }

        GameObject worldPrefab = itemInstance.GetDefinition().GetWorldPrefab();

        if (worldPrefab == null)
        {
            Debug.LogWarning("The item definition has no world prefab assigned.");
            return false;
        }

        GameObject worldObject = Instantiate(worldPrefab, position, rotation);

        WorldItem worldItem = worldObject.GetComponent<WorldItem>();
        if (worldItem == null)
        {
            worldItem = worldObject.GetComponentInChildren<WorldItem>();
        }

        if (worldItem != null)
        {
            worldItem.ApplyItemInstance(itemInstance.Clone());

            Rigidbody rigidbody = worldItem.GetRigidbody();
            if (rigidbody != null)
            {
                rigidbody.linearVelocity = linearVelocity;
                rigidbody.angularVelocity = angularVelocity;

                if (applyDropImpulse)
                {
                    Vector3 impulseDirection = rotation * Vector3.forward;
                    rigidbody.AddForce(impulseDirection.normalized * DropImpulse, ForceMode.Impulse);
                }
            }
        }

        Log("Spawned world item: " + itemInstance.GetDefinition().GetDisplayName());
        return true;
    }

    /// <summary>
    /// Selects a hotbar slot by index.
    /// </summary>
    public void SelectSlot(int slotIndex)
    {
        EnsureSlotListSize(SlotCount);

        if (!IsValidSlotIndex(slotIndex))
        {
            return;
        }

        if (slotIndex == SelectedIndex)
        {
            return;
        }

        SelectedIndex = slotIndex;
        RefreshEquippedItem();
        OnSelectedSlotChanged?.Invoke(SelectedIndex);

        Log("Selected slot index: " + SelectedIndex);
    }

    /// <summary>
    /// Refreshes the equipped object so it matches the currently selected slot.
    /// </summary>
    public void RefreshEquippedItem()
    {
        UnequipCurrentItem();
        EquipSelectedItem();
    }

    private void EnsureSlotListSize(int desiredSize)
    {
        desiredSize = Mathf.Max(1, desiredSize);

        while (Slots.Count < desiredSize)
        {
            Slots.Add(null);
        }

        while (Slots.Count > desiredSize)
        {
            Slots.RemoveAt(Slots.Count - 1);
        }

        SlotCount = desiredSize;
        SelectedIndex = Mathf.Clamp(SelectedIndex, 0, SlotCount - 1);
    }

    private void BindActions()
    {
        if (PlayerInput == null || PlayerInput.actions == null)
        {
            return;
        }

        HotbarScrollAction = PlayerInput.actions[HotbarScrollActionName];
        UsePrimaryAction = PlayerInput.actions[UsePrimaryActionName];
        UseSecondaryAction = PlayerInput.actions[UseSecondaryActionName];
        DropItemAction = PlayerInput.actions[DropItemActionName];

        RebuildSlotActions();
    }

    private void RebuildSlotActions()
    {
        SlotActions.Clear();

        if (PlayerInput == null || PlayerInput.actions == null)
        {
            return;
        }

        for (int index = 0; index < SlotCount && index < 9; index++)
        {
            string actionName = SlotActionPrefix + (index + 1);
            InputAction slotAction = PlayerInput.actions[actionName];
            SlotActions.Add(slotAction);
        }
    }

    private void HandleSelectionInput()
    {
        if (HotbarScrollAction != null)
        {
            float scrollValue = HotbarScrollAction.ReadValue<float>();

            if (Mathf.Abs(scrollValue) > 0.01f)
            {
                int direction = scrollValue > 0f ? 1 : -1;
                int nextIndex = SelectedIndex + direction;

                if (nextIndex < 0)
                {
                    nextIndex = SlotCount - 1;
                }
                else if (nextIndex >= SlotCount)
                {
                    nextIndex = 0;
                }

                SelectSlot(nextIndex);
            }
        }

        for (int index = 0; index < SlotActions.Count; index++)
        {
            InputAction slotAction = SlotActions[index];

            if (slotAction != null && slotAction.WasPressedThisFrame())
            {
                SelectSlot(index);
                return;
            }
        }
    }

    private void HandleUseInput()
    {
        if (CurrentEquippedBehaviour == null)
        {
            return;
        }

        if (UsePrimaryAction != null)
        {
            if (UsePrimaryAction.WasPressedThisFrame())
            {
                CurrentEquippedBehaviour.OnPrimaryUseStarted();
            }

            if (UsePrimaryAction.IsPressed())
            {
                CurrentEquippedBehaviour.OnPrimaryUseHeld();
            }

            if (UsePrimaryAction.WasReleasedThisFrame())
            {
                CurrentEquippedBehaviour.OnPrimaryUseEnded();
            }
        }

        if (UseSecondaryAction != null)
        {
            if (UseSecondaryAction.WasPressedThisFrame())
            {
                CurrentEquippedBehaviour.OnSecondaryUseStarted();
            }

            if (UseSecondaryAction.IsPressed())
            {
                CurrentEquippedBehaviour.OnSecondaryUseHeld();
            }

            if (UseSecondaryAction.WasReleasedThisFrame())
            {
                CurrentEquippedBehaviour.OnSecondaryUseEnded();
            }
        }
    }

    private void HandleDropInput()
    {
        if (DropItemAction == null)
        {
            return;
        }

        if (!DropItemAction.WasPressedThisFrame())
        {
            return;
        }

        DropSelectedItemToWorld();
    }

    private bool TryStackIntoSlot(ItemInstance incomingItem, int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            return false;
        }

        ItemInstance existingItem = Slots[slotIndex];

        if (existingItem == null || !existingItem.CanStackWith(incomingItem))
        {
            return false;
        }

        ItemDefinition definition = existingItem.GetDefinition();
        int maxStackSize = definition.GetMaxStackSize();
        int freeSpace = maxStackSize - existingItem.GetAmount();

        if (freeSpace <= 0)
        {
            return false;
        }

        int transferAmount = Mathf.Min(freeSpace, incomingItem.GetAmount());
        existingItem.SetAmount(existingItem.GetAmount() + transferAmount);
        incomingItem.SetAmount(incomingItem.GetAmount() - transferAmount);

        Log("Stacked item into slot: " + slotIndex);

        return incomingItem.GetAmount() <= 0;
    }

    private void EquipSelectedItem()
    {
        ItemInstance selectedItem = GetSelectedItem();

        if (selectedItem == null || selectedItem.GetDefinition() == null)
        {
            return;
        }

        GameObject equippedPrefab = selectedItem.GetDefinition().GetEquippedPrefab();

        if (equippedPrefab == null)
        {
            return;
        }

        Transform parent = EquippedItemSocket != null ? EquippedItemSocket : transform;
        CurrentEquippedObject = Instantiate(equippedPrefab, parent);
        CurrentEquippedObject.transform.localPosition = Vector3.zero;
        CurrentEquippedObject.transform.localRotation = Quaternion.identity;

        CurrentEquippedBehaviour = CurrentEquippedObject.GetComponent<EquippedItemBehaviour>();

        if (CurrentEquippedBehaviour != null)
        {
            CurrentEquippedBehaviour.Initialize(this, selectedItem);
            CurrentEquippedBehaviour.OnEquipped();
        }

        Log("Equipped item from slot: " + SelectedIndex);
    }

    private void UnequipCurrentItem()
    {
        if (CurrentEquippedBehaviour != null)
        {
            CurrentEquippedBehaviour.OnUnequipped();
        }

        if (CurrentEquippedObject != null)
        {
            Destroy(CurrentEquippedObject);
        }

        CurrentEquippedBehaviour = null;
        CurrentEquippedObject = null;
    }

    private void AutoEquipIfNeeded(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
        {
            return;
        }

        ItemInstance itemInstance = Slots[slotIndex];
        if (itemInstance == null || itemInstance.GetDefinition() == null)
        {
            return;
        }

        if (slotIndex != SelectedIndex)
        {
            return;
        }

        if (!itemInstance.GetDefinition().GetAutoEquipWhenSelected())
        {
            return;
        }

        RefreshEquippedItem();
    }

    private bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < Slots.Count;
    }

    private Vector3 GetDropSpawnPosition()
    {
        Vector3 origin = PlayerCamera != null ? PlayerCamera.transform.position : transform.position;
        Vector3 direction = GetDropDirection();
        Vector3 position = origin + (direction * DropForwardDistance) + (Vector3.up * DropVerticalOffset);

        if (DrawDropDebug)
        {
            Debug.DrawRay(origin, direction * DropForwardDistance, Color.yellow, 1f);
        }

        return position;
    }

    private Vector3 GetDropDirection()
    {
        if (PlayerCamera != null)
        {
            Vector3 cameraForward = PlayerCamera.transform.forward;
            cameraForward.Normalize();
            return cameraForward;
        }

        return transform.forward;
    }

    private void NotifySlotChanged(int slotIndex)
    {
        OnSlotChanged?.Invoke(slotIndex);
    }

    private void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[HotbarController] " + message);
    }
}
