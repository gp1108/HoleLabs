using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles runtime hotbar storage, selection, equipping, dropping and swapping.
/// This version routes every player input through PlayerInputReader instead of reading
/// PlayerInput actions directly, which keeps input ownership centralized.
/// 
/// Input ownership rule:
/// - Equipped items only receive use input while no higher-priority world interaction is capturing it.
/// - Lever dragging is considered a higher-priority interaction and blocks item use routing.
/// </summary>
public sealed class HotbarController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to calculate drop direction and helper defaults.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Socket used to parent equipped prefabs while selected.")]
    [SerializeField] private Transform EquippedItemSocket;

    [Tooltip("Optional lever interactor that can temporarily capture primary and secondary use input.")]
    [SerializeField] private LeverInteractor LeverInteractor;

    [Tooltip("Centralized player input reader used as the only input source for the hotbar.")]
    [SerializeField] private PlayerInputReader PlayerInputReader;

    [Header("Hotbar")]
    [Tooltip("Current number of slots available in the hotbar.")]
    [SerializeField] private int SlotCount = 8;

    [Tooltip("If true, the controller equips the currently selected slot on start.")]
    [SerializeField] private bool EquipSelectedSlotOnStart = true;

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
    /// Whether hotbar selection, use and drop input is currently blocked by an external modal state.
    /// </summary>
    private bool IsExternalHotbarInputBlocked;

    /// <summary>
    /// Previous-frame held state for primary use.
    /// </summary>
    private bool WasPrimaryHeldLastFrame;

    /// <summary>
    /// Previous-frame held state for secondary use.
    /// </summary>
    private bool WasSecondaryHeldLastFrame;

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
    /// Creates a deep-cloned snapshot of the current hotbar slots for saving.
    /// </summary>
    public List<ItemInstance> CreateSlotSaveSnapshot()
    {
        EnsureSlotListSize(SlotCount);

        List<ItemInstance> Result = new List<ItemInstance>(Slots.Count);

        for (int Index = 0; Index < Slots.Count; Index++)
        {
            Result.Add(Slots[Index] != null ? Slots[Index].Clone() : null);
        }

        return Result;
    }

    /// <summary>
    /// Restores the hotbar runtime state from save data.
    /// This fully replaces the current slot content and re-equips the selected slot.
    /// </summary>
    /// <param name="SavedSlots">Saved slot content.</param>
    /// <param name="SavedSelectedIndex">Saved selected slot index.</param>
    public void ApplySaveState(List<ItemInstance> SavedSlots, int SavedSelectedIndex)
    {
        ForceStopCurrentItemUsage();
        ResetUseTracking();
        UnequipCurrentItem();

        int DesiredSize = SavedSlots != null && SavedSlots.Count > 0
            ? SavedSlots.Count
            : Mathf.Max(1, SlotCount);

        EnsureSlotListSize(DesiredSize);

        for (int Index = 0; Index < Slots.Count; Index++)
        {
            if (SavedSlots != null && Index < SavedSlots.Count && SavedSlots[Index] != null)
            {
                Slots[Index] = SavedSlots[Index].Clone();
            }
            else
            {
                Slots[Index] = null;
            }

            NotifySlotChanged(Index);
        }

        SelectedIndex = Mathf.Clamp(SavedSelectedIndex, 0, Slots.Count - 1);

        RefreshEquippedItem();
        OnHotbarStructureChanged?.Invoke();
        OnSelectedSlotChanged?.Invoke(SelectedIndex);
    }

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
    /// Allows external systems to block or restore hotbar runtime input.
    /// </summary>
    /// <param name="IsBlocked">True to block hotbar input, false to restore it.</param>
    public void SetExternalHotbarInputBlocked(bool IsBlocked)
    {
        IsExternalHotbarInputBlocked = IsBlocked;

        if (IsBlocked)
        {
            ForceStopCurrentItemUsage();
            ResetUseTracking();
        }
    }

    /// <summary>
    /// Initializes references, allocates slots and optionally equips the selected slot.
    /// </summary>
    private void Awake()
    {
        if (PlayerCamera == null)
        {
            PlayerCamera = Camera.main;
        }

        if (LeverInteractor == null)
        {
            LeverInteractor = GetComponentInChildren<LeverInteractor>(true);
        }

        if (PlayerInputReader == null)
        {
            PlayerInputReader = GetComponent<PlayerInputReader>();
        }

        EnsureSlotListSize(SlotCount);

        if (EquipSelectedSlotOnStart)
        {
            EquipSelectedItem();
        }
    }

    /// <summary>
    /// Subscribes discrete input events from the centralized input reader.
    /// </summary>
    private void OnEnable()
    {
        if (PlayerInputReader == null)
        {
            return;
        }

        PlayerInputReader.DropItemPerformed += HandleDropItemPerformed;
        PlayerInputReader.SlotPerformed += HandleSlotPerformed;
        PlayerInputReader.PreviousPerformed += HandlePreviousPerformed;
        PlayerInputReader.NextPerformed += HandleNextPerformed;
    }

    /// <summary>
    /// Unsubscribes discrete input events from the centralized input reader.
    /// </summary>
    private void OnDisable()
    {
        if (PlayerInputReader != null)
        {
            PlayerInputReader.DropItemPerformed -= HandleDropItemPerformed;
            PlayerInputReader.SlotPerformed -= HandleSlotPerformed;
            PlayerInputReader.PreviousPerformed -= HandlePreviousPerformed;
            PlayerInputReader.NextPerformed -= HandleNextPerformed;
        }

        ResetUseTracking();
    }

    /// <summary>
    /// Processes selection and use input every frame through PlayerInputReader.
    /// </summary>
    private void Update()
    {
        if (IsExternalHotbarInputBlocked)
        {
            return;
        }

        HandleScrollSelectionInput();
        HandleUseInput();
    }

    /// <summary>
    /// Resizes the hotbar and keeps the selected index valid.
    /// </summary>
    /// <param name="NewSlotCount">Requested new slot count.</param>
    public void ResizeHotbar(int NewSlotCount)
    {
        SlotCount = Mathf.Max(1, NewSlotCount);
        EnsureSlotListSize(SlotCount);

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
/// Tries to consume an amount from the currently selected hotbar stack.
/// This is intended for build systems or tools that spend material directly from the equipped slot.
/// </summary>
/// <param name="AmountToConsume">Amount that should be removed from the selected slot.</param>
/// <returns>True when the requested amount was consumed successfully.</returns>
public bool TryConsumeSelectedItemAmount(int AmountToConsume)
{
    return TryConsumeSelectedItemAmount(AmountToConsume, null);
}

/// <summary>
/// Tries to consume an amount from the currently selected hotbar stack while also validating the expected definition.
/// When the remaining amount reaches zero, the selected slot is cleared and the equipped item is refreshed automatically.
/// </summary>
/// <param name="AmountToConsume">Amount that should be removed from the selected slot.</param>
/// <param name="RequiredDefinition">Optional required definition that must still match the selected slot.</param>
/// <returns>True when the requested amount was consumed successfully.</returns>
public bool TryConsumeSelectedItemAmount(int AmountToConsume, ItemDefinition RequiredDefinition)
{
    EnsureSlotListSize(SlotCount);

    if (AmountToConsume <= 0)
    {
        return true;
    }

    if (!IsValidSlotIndex(SelectedIndex))
    {
        return false;
    }

    ItemInstance SelectedItem = Slots[SelectedIndex];

    if (SelectedItem == null || SelectedItem.GetDefinition() == null)
    {
        return false;
    }

    if (RequiredDefinition != null && SelectedItem.GetDefinition() != RequiredDefinition)
    {
        return false;
    }

    int CurrentAmount = SelectedItem.GetAmount();
    if (CurrentAmount < AmountToConsume)
    {
        return false;
    }

    int RemainingAmount = CurrentAmount - AmountToConsume;
    if (RemainingAmount > 0)
    {
        SelectedItem.SetAmount(RemainingAmount);
        NotifySlotChanged(SelectedIndex);
        Log("Consumed " + AmountToConsume + " item(s) from selected slot. Remaining amount: " + RemainingAmount);
        return true;
    }

    Slots[SelectedIndex] = null;
    RefreshEquippedItem();
    NotifySlotChanged(SelectedIndex);
    Log("Consumed the full selected item stack.");
    return true;
}

    /// <summary>
    /// Drops the selected item into the world using the configured drop helpers.
    /// </summary>
    public bool DropSelectedItemToWorld()
    {
        ItemInstance ItemToDrop = RemoveSelectedItem();

        if (ItemToDrop == null)
        {
            return false;
        }

        return SpawnWorldItem(
            ItemToDrop,
            GetDropSpawnPosition(),
            Quaternion.LookRotation(GetDropDirection(), Vector3.up),
            Vector3.zero,
            Vector3.zero,
            true);
    }

    /// <summary>
    /// Spawns a world item using a forward direction.
    /// </summary>
    public bool SpawnWorldItem(ItemInstance ItemInstance, Vector3 Position, Vector3 Direction)
    {
        Quaternion Rotation = Quaternion.LookRotation(
            Direction.sqrMagnitude > 0.0001f ? Direction : transform.forward,
            Vector3.up);

        return SpawnWorldItem(ItemInstance, Position, Rotation, Vector3.zero, Vector3.zero, true);
    }

    /// <summary>
    /// Spawns a world item with explicit transform and rigidbody settings.
    /// </summary>
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

        ForceStopCurrentItemUsage();
        ResetUseTracking();

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
    /// Processes scroll-based slot selection from PlayerInputReader.
    /// </summary>
    private void HandleScrollSelectionInput()
    {
        if (PlayerInputReader == null)
        {
            return;
        }

        float ScrollValue = PlayerInputReader.HotbarScroll;

        if (Mathf.Abs(ScrollValue) <= 0.01f)
        {
            return;
        }

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

    /// <summary>
    /// Routes primary and secondary use input to the equipped item using the centralized input reader.
    /// </summary>
    private void HandleUseInput()
    {
        if (CurrentEquippedBehaviour == null || PlayerInputReader == null)
        {
            ResetUseTracking();
            WasItemUseBlockedLastFrame = false;
            return;
        }

        bool IsPrimaryHeldNow = PlayerInputReader.IsUsePrimaryHeld;
        bool IsSecondaryHeldNow = PlayerInputReader.IsUseSecondaryHeld;

        if (IsUseInputCapturedByExternalInteraction())
        {
            if (!WasItemUseBlockedLastFrame)
            {
                CurrentEquippedBehaviour.ForceStopItemUsage();
                WasItemUseBlockedLastFrame = true;
                Log("Blocked equipped item use because a higher-priority interaction captured input.");
            }

            WasPrimaryHeldLastFrame = IsPrimaryHeldNow;
            WasSecondaryHeldLastFrame = IsSecondaryHeldNow;
            return;
        }

        WasItemUseBlockedLastFrame = false;

        if (IsPrimaryHeldNow && !WasPrimaryHeldLastFrame)
        {
            CurrentEquippedBehaviour.OnPrimaryUseStarted();
        }

        if (IsPrimaryHeldNow)
        {
            CurrentEquippedBehaviour.OnPrimaryUseHeld();
        }

        if (!IsPrimaryHeldNow && WasPrimaryHeldLastFrame)
        {
            CurrentEquippedBehaviour.OnPrimaryUseEnded();
        }

        if (IsSecondaryHeldNow && !WasSecondaryHeldLastFrame)
        {
            CurrentEquippedBehaviour.OnSecondaryUseStarted();
        }

        if (IsSecondaryHeldNow)
        {
            CurrentEquippedBehaviour.OnSecondaryUseHeld();
        }

        if (!IsSecondaryHeldNow && WasSecondaryHeldLastFrame)
        {
            CurrentEquippedBehaviour.OnSecondaryUseEnded();
        }

        WasPrimaryHeldLastFrame = IsPrimaryHeldNow;
        WasSecondaryHeldLastFrame = IsSecondaryHeldNow;
    }

    /// <summary>
    /// Returns whether an external world interaction currently owns the use input.
    /// </summary>
    private bool IsUseInputCapturedByExternalInteraction()
    {
        if (LeverInteractor == null)
        {
            return false;
        }

        return LeverInteractor.IsCapturingPrimaryInput;
    }

    /// <summary>
    /// Handles the drop item discrete input coming from PlayerInputReader.
    /// </summary>
    private void HandleDropItemPerformed()
    {
        if (IsExternalHotbarInputBlocked)
        {
            return;
        }

        DropSelectedItemToWorld();
    }

    /// <summary>
    /// Handles direct slot selection from PlayerInputReader.
    /// </summary>
    /// <param name="SlotNumber">One-based slot number.</param>
    private void HandleSlotPerformed(int SlotNumber)
    {
        if (IsExternalHotbarInputBlocked)
        {
            return;
        }

        int SlotIndex = SlotNumber - 1;
        SelectSlot(SlotIndex);
    }

    /// <summary>
    /// Handles previous input as one slot step to the left.
    /// </summary>
    private void HandlePreviousPerformed()
    {
        if (IsExternalHotbarInputBlocked)
        {
            return;
        }

        int PreviousIndex = SelectedIndex - 1;

        if (PreviousIndex < 0)
        {
            PreviousIndex = SlotCount - 1;
        }

        SelectSlot(PreviousIndex);
    }

    /// <summary>
    /// Handles next input as one slot step to the right.
    /// </summary>
    private void HandleNextPerformed()
    {
        if (IsExternalHotbarInputBlocked)
        {
            return;
        }

        int NextIndex = SelectedIndex + 1;

        if (NextIndex >= SlotCount)
        {
            NextIndex = 0;
        }

        SelectSlot(NextIndex);
    }

    /// <summary>
    /// Tries to stack the incoming item into the specified slot.
    /// </summary>
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
    private bool IsValidSlotIndex(int SlotIndex)
    {
        return SlotIndex >= 0 && SlotIndex < Slots.Count;
    }

    /// <summary>
    /// Computes the world spawn position for dropped items.
    /// </summary>
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
    private void NotifySlotChanged(int SlotIndex)
    {
        OnSlotChanged?.Invoke(SlotIndex);
    }

    /// <summary>
    /// Force-stops the currently equipped item usage if any item is equipped.
    /// </summary>
    private void ForceStopCurrentItemUsage()
    {
        if (CurrentEquippedBehaviour != null)
        {
            CurrentEquippedBehaviour.ForceStopItemUsage();
        }
    }

    /// <summary>
    /// Resets local use-state tracking used to derive start and end edges from held booleans.
    /// </summary>
    private void ResetUseTracking()
    {
        WasPrimaryHeldLastFrame = false;
        WasSecondaryHeldLastFrame = false;
    }

    /// <summary>
    /// Writes a hotbar-specific debug log if enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[HotbarController] " + Message, this);
    }
}