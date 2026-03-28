using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles runtime hotbar storage, selection, equipping, dropping and swapping.
/// This version exposes UI notifications so the hotbar can update without polling heavy logic.
/// It also safely interrupts equipped items before they are destroyed so animations,
/// VFX and tool states do not get stuck when switching slots quickly.
/// 
/// Input ownership rule:
/// - Equipped items only receive use input while no higher-priority world interaction is capturing it.
/// - Lever dragging is considered a higher-priority interaction and blocks item use routing.
/// </summary>
[RequireComponent(typeof(PlayerInput))]
public sealed class HotbarController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to calculate drop direction and helper defaults.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Socket used to parent equipped prefabs while selected.")]
    [SerializeField] private Transform EquippedItemSocket;

    [Tooltip("Optional lever interactor that can temporarily capture primary and secondary use input.")]
    [SerializeField] private LeverInteractor LeverInteractor;

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

    [Header("Scroll")]
    [Tooltip("Minimum time between scroll inputs to avoid overly fast slot switching.")]
    [SerializeField] private float ScrollCooldown = 0.1f;

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

    /// <summary>
    /// Cached PlayerInput component used to resolve input actions.
    /// </summary>
    private PlayerInput PlayerInput;

    /// <summary>
    /// Input action used to scroll through hotbar slots.
    /// </summary>
    private InputAction HotbarScrollAction;

    /// <summary>
    /// Input action used for the equipped item's primary use.
    /// </summary>
    private InputAction UsePrimaryAction;

    /// <summary>
    /// Input action used for the equipped item's secondary use.
    /// </summary>
    private InputAction UseSecondaryAction;

    /// <summary>
    /// Input action used to drop the selected item.
    /// </summary>
    private InputAction DropItemAction;

    /// <summary>
    /// Cached direct slot actions such as Slot1, Slot2 and so on.
    /// </summary>
    private readonly List<InputAction> SlotActions = new List<InputAction>();

    /// <summary>
    /// Currently spawned equipped object instance.
    /// </summary>
    private GameObject CurrentEquippedObject;

    /// <summary>
    /// Equipped behaviour currently owned by the selected slot.
    /// </summary>
    private EquippedItemBehaviour CurrentEquippedBehaviour;

    /// <summary>
    /// Last time a scroll input was accepted.
    /// </summary>
    private float LastScrollTime;

    /// <summary>
    /// Whether the primary use state was forcibly interrupted because another interaction captured input.
    /// This prevents repeatedly calling ForceStopItemUsage every frame.
    /// </summary>
    private bool WasItemUseBlockedLastFrame;

    /// <summary>
    /// Invoked when a slot content changes.
    /// </summary>
    public event Action<int> OnSlotChanged;

    /// <summary>
    /// Invoked when the selected hotbar slot changes.
    /// </summary>
    public event Action<int> OnSelectedSlotChanged;

    /// <summary>
    /// Invoked when the hotbar structure changes, for example after resizing.
    /// </summary>
    public event Action OnHotbarStructureChanged;

    /// <summary>
    /// Gets the currently selected slot index.
    /// </summary>
    public int GetSelectedIndex()
    {
        return SelectedIndex;
    }

    /// <summary>
    /// Gets the current number of hotbar slots.
    /// </summary>
    public int GetSlotCount()
    {
        return SlotCount;
    }

    /// <summary>
    /// Gets the equipped item behaviour currently instantiated for the selected slot.
    /// This is used by higher-priority interaction systems to safely interrupt item usage.
    /// </summary>
    public EquippedItemBehaviour GetCurrentEquippedItemBehaviour()
    {
        return CurrentEquippedBehaviour;
    }

    /// <summary>
    /// Gets the item instance stored at the given slot index.
    /// </summary>
    /// <param name="SlotIndex">Target hotbar slot index.</param>
    /// <returns>Stored runtime item instance or null.</returns>
    public ItemInstance GetItemAtSlot(int SlotIndex)
    {
        if (!IsValidSlotIndex(SlotIndex))
        {
            return null;
        }

        return Slots[SlotIndex];
    }

    /// <summary>
    /// Initializes references, allocates slots and optionally equips the selected slot.
    /// </summary>
    private void Awake()
    {
        PlayerInput = GetComponent<PlayerInput>();

        if (PlayerCamera == null)
        {
            PlayerCamera = Camera.main;
        }

        if (LeverInteractor == null)
        {
            LeverInteractor = GetComponentInChildren<LeverInteractor>(true);
        }

        EnsureSlotListSize(SlotCount);
        BindActions();

        if (EquipSelectedSlotOnStart)
        {
            EquipSelectedItem();
        }
    }

    /// <summary>
    /// Processes selection, use and drop input every frame.
    /// </summary>
    private void Update()
    {
        HandleSelectionInput();
        HandleUseInput();
        HandleDropInput();
    }

    /// <summary>
    /// Resizes the hotbar and rebuilds direct slot input bindings.
    /// </summary>
    /// <param name="NewSlotCount">Requested new slot count.</param>
    public void ResizeHotbar(int NewSlotCount)
    {
        SlotCount = Mathf.Max(1, NewSlotCount);
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
    /// Returns whether the selected slot currently contains an item.
    /// </summary>
    public bool HasSelectedItem()
    {
        return GetSelectedItem() != null;
    }

    /// <summary>
    /// Gets the runtime item instance currently stored in the selected slot.
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
    /// Tries to add an item instance to the hotbar, preferring stacking and then empty slots.
    /// </summary>
    /// <param name="ItemInstance">Incoming runtime item instance.</param>
    /// <param name="PreferredSlotIndex">Preferred slot to receive the item.</param>
    /// <param name="InsertedSlotIndex">Resolved slot that received the item.</param>
    /// <returns>True if the full item instance was inserted.</returns>
    public bool TryAddItem(ItemInstance ItemInstance, int PreferredSlotIndex, out int InsertedSlotIndex)
    {
        InsertedSlotIndex = -1;

        if (ItemInstance == null || ItemInstance.GetDefinition() == null)
        {
            return false;
        }

        EnsureSlotListSize(SlotCount);

        if (TryStackIntoSlot(ItemInstance, PreferredSlotIndex))
        {
            InsertedSlotIndex = PreferredSlotIndex;
            NotifySlotChanged(InsertedSlotIndex);
            return true;
        }

        for (int Index = 0; Index < Slots.Count; Index++)
        {
            if (Index == PreferredSlotIndex)
            {
                continue;
            }

            if (TryStackIntoSlot(ItemInstance, Index))
            {
                InsertedSlotIndex = Index;
                NotifySlotChanged(InsertedSlotIndex);
                return true;
            }
        }

        if (IsValidSlotIndex(PreferredSlotIndex) && Slots[PreferredSlotIndex] == null)
        {
            Slots[PreferredSlotIndex] = ItemInstance.Clone();
            InsertedSlotIndex = PreferredSlotIndex;
            AutoEquipIfNeeded(InsertedSlotIndex);
            NotifySlotChanged(InsertedSlotIndex);
            Log("Added item to preferred empty slot: " + InsertedSlotIndex);
            return true;
        }

        for (int Index = 0; Index < Slots.Count; Index++)
        {
            if (Slots[Index] != null)
            {
                continue;
            }

            Slots[Index] = ItemInstance.Clone();
            InsertedSlotIndex = Index;
            AutoEquipIfNeeded(InsertedSlotIndex);
            NotifySlotChanged(InsertedSlotIndex);
            Log("Added item to first free slot: " + InsertedSlotIndex);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Replaces the currently selected item and returns the previous one.
    /// </summary>
    /// <param name="NewItemInstance">New runtime item instance.</param>
    /// <returns>Previous runtime item instance or null.</returns>
    public ItemInstance ReplaceSelectedItem(ItemInstance NewItemInstance)
    {
        if (NewItemInstance == null)
        {
            return null;
        }

        EnsureSlotListSize(SlotCount);

        ItemInstance PreviousItem = Slots[SelectedIndex];
        Slots[SelectedIndex] = NewItemInstance.Clone();
        RefreshEquippedItem();
        NotifySlotChanged(SelectedIndex);

        Log("Replaced selected slot item at index: " + SelectedIndex);
        return PreviousItem;
    }

    /// <summary>
    /// Removes and returns the item currently stored in the selected slot.
    /// </summary>
    /// <returns>Removed runtime item instance or null.</returns>
    public ItemInstance RemoveSelectedItem()
    {
        EnsureSlotListSize(SlotCount);

        ItemInstance RemovedItem = Slots[SelectedIndex];
        Slots[SelectedIndex] = null;
        RefreshEquippedItem();
        NotifySlotChanged(SelectedIndex);

        Log("Removed selected item at index: " + SelectedIndex);
        return RemovedItem;
    }

    /// <summary>
    /// Drops the selected item into the world using the configured drop helpers.
    /// </summary>
    /// <returns>True when the selected item was successfully dropped.</returns>
    public bool DropSelectedItemToWorld()
    {
        ItemInstance ItemToDrop = RemoveSelectedItem();

        if (ItemToDrop == null)
        {
            return false;
        }

        return SpawnWorldItem(ItemToDrop, GetDropSpawnPosition(), Quaternion.LookRotation(GetDropDirection(), Vector3.up), Vector3.zero, Vector3.zero, true);
    }

    /// <summary>
    /// Spawns a world item using a forward direction.
    /// </summary>
    /// <param name="ItemInstance">Runtime item to spawn.</param>
    /// <param name="Position">Spawn position.</param>
    /// <param name="Direction">Facing direction.</param>
    /// <returns>True when the item was spawned successfully.</returns>
    public bool SpawnWorldItem(ItemInstance ItemInstance, Vector3 Position, Vector3 Direction)
    {
        Quaternion Rotation = Quaternion.LookRotation(Direction.sqrMagnitude > 0.0001f ? Direction : transform.forward, Vector3.up);
        return SpawnWorldItem(ItemInstance, Position, Rotation, Vector3.zero, Vector3.zero, true);
    }

    /// <summary>
    /// Spawns a world item with explicit transform and rigidbody settings.
    /// </summary>
    /// <param name="ItemInstance">Runtime item to spawn.</param>
    /// <param name="Position">Spawn position.</param>
    /// <param name="Rotation">Spawn rotation.</param>
    /// <param name="LinearVelocity">Initial rigidbody linear velocity.</param>
    /// <param name="AngularVelocity">Initial rigidbody angular velocity.</param>
    /// <param name="ApplyDropImpulse">Whether to add the configured forward impulse.</param>
    /// <returns>True when the item was spawned successfully.</returns>
    public bool SpawnWorldItem(ItemInstance ItemInstance, Vector3 Position, Quaternion Rotation, Vector3 LinearVelocity, Vector3 AngularVelocity, bool ApplyDropImpulse)
    {
        if (ItemInstance == null || ItemInstance.GetDefinition() == null)
        {
            return false;
        }

        GameObject WorldPrefab = ItemInstance.GetDefinition().GetWorldPrefab();

        if (WorldPrefab == null)
        {
            Debug.LogWarning("The item definition has no world prefab assigned.");
            return false;
        }

        GameObject WorldObject = Instantiate(WorldPrefab, Position, Rotation);

        WorldItem WorldItem = WorldObject.GetComponent<WorldItem>();
        if (WorldItem == null)
        {
            WorldItem = WorldObject.GetComponentInChildren<WorldItem>();
        }

        if (WorldItem != null)
        {
            WorldItem.ApplyItemInstance(ItemInstance.Clone());

            Rigidbody RigidbodyComponent = WorldItem.GetRigidbody();
            if (RigidbodyComponent != null)
            {
                RigidbodyComponent.linearVelocity = LinearVelocity;
                RigidbodyComponent.angularVelocity = AngularVelocity;

                if (ApplyDropImpulse)
                {
                    Vector3 ImpulseDirection = Rotation * Vector3.forward;
                    RigidbodyComponent.AddForce(ImpulseDirection.normalized * DropImpulse, ForceMode.Impulse);
                }
            }
        }

        Log("Spawned world item: " + ItemInstance.GetDefinition().GetDisplayName());
        return true;
    }

    /// <summary>
    /// Selects a hotbar slot and refreshes the equipped object if needed.
    /// </summary>
    /// <param name="SlotIndex">Target slot index.</param>
    public void SelectSlot(int SlotIndex)
    {
        EnsureSlotListSize(SlotCount);

        if (!IsValidSlotIndex(SlotIndex))
        {
            return;
        }

        if (SlotIndex == SelectedIndex)
        {
            return;
        }

        SelectedIndex = SlotIndex;
        RefreshEquippedItem();
        OnSelectedSlotChanged?.Invoke(SelectedIndex);

        Log("Selected slot index: " + SelectedIndex);
    }

    /// <summary>
    /// Unequips the current equipped object and re-equips the selected slot item.
    /// </summary>
    public void RefreshEquippedItem()
    {
        UnequipCurrentItem();
        EquipSelectedItem();
    }

    /// <summary>
    /// Ensures the internal slot list exactly matches the desired size.
    /// </summary>
    /// <param name="DesiredSize">Target slot count.</param>
    private void EnsureSlotListSize(int DesiredSize)
    {
        DesiredSize = Mathf.Max(1, DesiredSize);

        while (Slots.Count < DesiredSize)
        {
            Slots.Add(null);
        }

        while (Slots.Count > DesiredSize)
        {
            Slots.RemoveAt(Slots.Count - 1);
        }

        SlotCount = DesiredSize;
        SelectedIndex = Mathf.Clamp(SelectedIndex, 0, SlotCount - 1);
    }

    /// <summary>
    /// Resolves input actions from the PlayerInput asset.
    /// </summary>
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

    /// <summary>
    /// Rebuilds direct slot action bindings after the slot count changes.
    /// </summary>
    private void RebuildSlotActions()
    {
        SlotActions.Clear();

        if (PlayerInput == null || PlayerInput.actions == null)
        {
            return;
        }

        for (int Index = 0; Index < SlotCount && Index < 9; Index++)
        {
            string ActionName = SlotActionPrefix + (Index + 1);
            InputAction SlotAction = PlayerInput.actions[ActionName];
            SlotActions.Add(SlotAction);
        }
    }

    /// <summary>
    /// Processes scroll and direct slot selection input.
    /// </summary>
    private void HandleSelectionInput()
    {
        if (HotbarScrollAction != null)
        {
            float ScrollValue = HotbarScrollAction.ReadValue<float>();

            if (Mathf.Abs(ScrollValue) > 0.01f)
            {
                if (Time.time - LastScrollTime < ScrollCooldown)
                {
                    return;
                }

                LastScrollTime = Time.time;

                int Direction = ScrollValue > 0f ? 1 : -1;
                int NextIndex = SelectedIndex + Direction;

                if (NextIndex < 0)
                {
                    NextIndex = SlotCount - 1;
                }
                else if (NextIndex >= SlotCount)
                {
                    NextIndex = 0;
                }

                SelectSlot(NextIndex);
            }
        }

        for (int Index = 0; Index < SlotActions.Count; Index++)
        {
            InputAction SlotAction = SlotActions[Index];

            if (SlotAction != null && SlotAction.WasPressedThisFrame())
            {
                SelectSlot(Index);
                return;
            }
        }
    }

    /// <summary>
    /// Routes primary and secondary use input to the equipped item only while no higher-priority
    /// interaction is currently capturing the use buttons.
    /// </summary>
    private void HandleUseInput()
    {
        if (CurrentEquippedBehaviour == null)
        {
            WasItemUseBlockedLastFrame = false;
            return;
        }

        if (IsUseInputCapturedByExternalInteraction())
        {
            if (!WasItemUseBlockedLastFrame)
            {
                CurrentEquippedBehaviour.ForceStopItemUsage();
                WasItemUseBlockedLastFrame = true;
                Log("Blocked equipped item use because a higher-priority interaction captured input.");
            }

            return;
        }

        WasItemUseBlockedLastFrame = false;

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

    /// <summary>
    /// Returns whether an external world interaction currently owns the use input.
    /// </summary>
    /// <returns>True when the equipped item must not receive use input this frame.</returns>
    private bool IsUseInputCapturedByExternalInteraction()
    {
        if (LeverInteractor == null)
        {
            return false;
        }

        return LeverInteractor.IsCapturingPrimaryInput;
    }

    /// <summary>
    /// Processes the drop action input.
    /// </summary>
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

    /// <summary>
    /// Tries to stack the incoming item into the specified slot.
    /// </summary>
    /// <param name="IncomingItem">Incoming runtime item instance.</param>
    /// <param name="SlotIndex">Target slot index.</param>
    /// <returns>True when the full incoming amount was stacked.</returns>
    private bool TryStackIntoSlot(ItemInstance IncomingItem, int SlotIndex)
    {
        if (!IsValidSlotIndex(SlotIndex))
        {
            return false;
        }

        ItemInstance ExistingItem = Slots[SlotIndex];

        if (ExistingItem == null || !ExistingItem.CanStackWith(IncomingItem))
        {
            return false;
        }

        ItemDefinition Definition = ExistingItem.GetDefinition();
        int MaxStackSize = Definition.GetMaxStackSize();
        int FreeSpace = MaxStackSize - ExistingItem.GetAmount();

        if (FreeSpace <= 0)
        {
            return false;
        }

        int TransferAmount = Mathf.Min(FreeSpace, IncomingItem.GetAmount());
        ExistingItem.SetAmount(ExistingItem.GetAmount() + TransferAmount);
        IncomingItem.SetAmount(IncomingItem.GetAmount() - TransferAmount);

        Log("Stacked item into slot: " + SlotIndex);

        return IncomingItem.GetAmount() <= 0;
    }

    /// <summary>
    /// Instantiates and initializes the equipped prefab of the selected slot item.
    /// </summary>
    private void EquipSelectedItem()
    {
        ItemInstance SelectedItem = GetSelectedItem();

        if (SelectedItem == null || SelectedItem.GetDefinition() == null)
        {
            return;
        }

        GameObject EquippedPrefab = SelectedItem.GetDefinition().GetEquippedPrefab();

        if (EquippedPrefab == null)
        {
            return;
        }

        Transform Parent = EquippedItemSocket != null ? EquippedItemSocket : transform;
        CurrentEquippedObject = Instantiate(EquippedPrefab, Parent);
        CurrentEquippedObject.transform.localPosition = Vector3.zero;
        CurrentEquippedObject.transform.localRotation = Quaternion.identity;

        CurrentEquippedBehaviour = CurrentEquippedObject.GetComponent<EquippedItemBehaviour>();

        if (CurrentEquippedBehaviour != null)
        {
            CurrentEquippedBehaviour.Initialize(this, SelectedItem);
            CurrentEquippedBehaviour.OnEquipped();
        }

        Log("Equipped item from slot: " + SelectedIndex);
    }

    /// <summary>
    /// Safely interrupts and destroys the current equipped object.
    /// </summary>
    private void UnequipCurrentItem()
    {
        if (CurrentEquippedBehaviour != null)
        {
            CurrentEquippedBehaviour.ForceStopItemUsage();
            CurrentEquippedBehaviour.OnUnequipped();
        }

        if (CurrentEquippedObject != null)
        {
            Destroy(CurrentEquippedObject);
        }

        CurrentEquippedBehaviour = null;
        CurrentEquippedObject = null;
        WasItemUseBlockedLastFrame = false;
    }

    /// <summary>
    /// Auto-equips the given slot item if it matches the current selection and supports auto-equip.
    /// </summary>
    /// <param name="SlotIndex">Slot index to evaluate.</param>
    private void AutoEquipIfNeeded(int SlotIndex)
    {
        if (!IsValidSlotIndex(SlotIndex))
        {
            return;
        }

        ItemInstance ItemInstance = Slots[SlotIndex];
        if (ItemInstance == null || ItemInstance.GetDefinition() == null)
        {
            return;
        }

        if (SlotIndex != SelectedIndex)
        {
            return;
        }

        if (!ItemInstance.GetDefinition().GetAutoEquipWhenSelected())
        {
            return;
        }

        RefreshEquippedItem();
    }

    /// <summary>
    /// Returns whether the provided slot index is valid.
    /// </summary>
    /// <param name="SlotIndex">Slot index to validate.</param>
    /// <returns>True when the index is inside the valid slot range.</returns>
    private bool IsValidSlotIndex(int SlotIndex)
    {
        return SlotIndex >= 0 && SlotIndex < Slots.Count;
    }

    /// <summary>
    /// Computes the world spawn position for dropped items.
    /// </summary>
    /// <returns>Drop spawn position in world space.</returns>
    private Vector3 GetDropSpawnPosition()
    {
        Vector3 Origin = PlayerCamera != null ? PlayerCamera.transform.position : transform.position;
        Vector3 Direction = GetDropDirection();
        Vector3 Position = Origin + (Direction * DropForwardDistance) + (Vector3.up * DropVerticalOffset);

        if (DrawDropDebug)
        {
            Debug.DrawRay(Origin, Direction * DropForwardDistance, Color.yellow, 1f);
        }

        return Position;
    }

    /// <summary>
    /// Returns the forward direction used to drop items into the world.
    /// </summary>
    /// <returns>Normalized drop direction.</returns>
    private Vector3 GetDropDirection()
    {
        if (PlayerCamera != null)
        {
            Vector3 CameraForward = PlayerCamera.transform.forward;
            CameraForward.Normalize();
            return CameraForward;
        }

        return transform.forward;
    }

    /// <summary>
    /// Notifies listeners that a specific slot changed.
    /// </summary>
    /// <param name="SlotIndex">Changed slot index.</param>
    private void NotifySlotChanged(int SlotIndex)
    {
        OnSlotChanged?.Invoke(SlotIndex);
    }

    /// <summary>
    /// Writes a hotbar-specific debug log if enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[HotbarController] " + Message, this);
    }
}