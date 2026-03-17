using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rebuilds and updates the hotbar UI based on the runtime state of the hotbar controller.
/// It listens to events instead of polling all data every frame.
/// </summary>
public sealed class HotbarUIController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Hotbar controller used as the data source for this UI.")]
    [SerializeField] private HotbarController HotbarController;

    [Tooltip("Parent transform that will contain all generated slot UI instances.")]
    [SerializeField] private Transform SlotContainer;

    [Tooltip("Prefab used to create each visual hotbar slot.")]
    [SerializeField] private HotbarSlotUI SlotPrefab;

    [Header("Behaviour")]
    [Tooltip("If true, the UI rebuilds itself automatically on Awake.")]
    [SerializeField] private bool RebuildOnAwake = true;

    [Tooltip("If true, the slot labels start at 1 instead of 0.")]
    [SerializeField] private bool UseHumanReadableSlotNumbers = true;

    private readonly List<HotbarSlotUI> SpawnedSlots = new List<HotbarSlotUI>();

    /// <summary>
    /// Initializes the hotbar UI and subscribes to data events.
    /// </summary>
    private void Awake()
    {
        if (HotbarController == null)
        {
            HotbarController = FindFirstObjectByType<HotbarController>();
        }

        SubscribeToHotbarEvents();

        if (RebuildOnAwake)
        {
            RebuildAllSlots();
            RefreshAllSlots();
        }
    }

    /// <summary>
    /// Unsubscribes from hotbar events when destroyed.
    /// </summary>
    private void OnDestroy()
    {
        UnsubscribeFromHotbarEvents();
    }

    /// <summary>
    /// Rebuilds the slot visuals to match the current hotbar slot count.
    /// </summary>
    public void RebuildAllSlots()
    {
        ClearSpawnedSlots();

        if (HotbarController == null || SlotContainer == null || SlotPrefab == null)
        {
            return;
        }

        int slotCount = HotbarController.GetSlotCount();

        for (int index = 0; index < slotCount; index++)
        {
            HotbarSlotUI slotUI = Instantiate(SlotPrefab, SlotContainer);
            int slotNumber = UseHumanReadableSlotNumbers ? index + 1 : index;
            slotUI.SetSlotIndexLabel(slotNumber);
            SpawnedSlots.Add(slotUI);
        }
    }

    /// <summary>
    /// Refreshes all visual slot content and selection state.
    /// </summary>
    public void RefreshAllSlots()
    {
        if (HotbarController == null)
        {
            return;
        }

        for (int index = 0; index < SpawnedSlots.Count; index++)
        {
            RefreshSlot(index);
        }
    }

    /// <summary>
    /// Refreshes a single slot using the current hotbar state.
    /// </summary>
    public void RefreshSlot(int slotIndex)
    {
        if (HotbarController == null)
        {
            return;
        }

        if (slotIndex < 0 || slotIndex >= SpawnedSlots.Count)
        {
            return;
        }

        HotbarSlotUI slotUI = SpawnedSlots[slotIndex];
        ItemInstance itemInstance = HotbarController.GetItemAtSlot(slotIndex);

        slotUI.SetItem(itemInstance);
        slotUI.SetSelected(slotIndex == HotbarController.GetSelectedIndex());
    }

    /// <summary>
    /// Subscribes to all hotbar events required by this UI.
    /// </summary>
    private void SubscribeToHotbarEvents()
    {
        if (HotbarController == null)
        {
            return;
        }

        HotbarController.OnSlotChanged += HandleSlotChanged;
        HotbarController.OnSelectedSlotChanged += HandleSelectedSlotChanged;
        HotbarController.OnHotbarStructureChanged += HandleHotbarStructureChanged;
    }

    /// <summary>
    /// Unsubscribes from hotbar events.
    /// </summary>
    private void UnsubscribeFromHotbarEvents()
    {
        if (HotbarController == null)
        {
            return;
        }

        HotbarController.OnSlotChanged -= HandleSlotChanged;
        HotbarController.OnSelectedSlotChanged -= HandleSelectedSlotChanged;
        HotbarController.OnHotbarStructureChanged -= HandleHotbarStructureChanged;
    }

    /// <summary>
    /// Clears all spawned slot UI instances.
    /// </summary>
    private void ClearSpawnedSlots()
    {
        for (int index = 0; index < SpawnedSlots.Count; index++)
        {
            if (SpawnedSlots[index] != null)
            {
                Destroy(SpawnedSlots[index].gameObject);
            }
        }

        SpawnedSlots.Clear();
    }

    /// <summary>
    /// Handles slot content changes from the hotbar.
    /// </summary>
    private void HandleSlotChanged(int slotIndex)
    {
        RefreshSlot(slotIndex);
    }

    /// <summary>
    /// Handles selection changes from the hotbar.
    /// </summary>
    private void HandleSelectedSlotChanged(int selectedSlotIndex)
    {
        RefreshAllSlots();
    }

    /// <summary>
    /// Handles hotbar structural changes such as resizing.
    /// </summary>
    private void HandleHotbarStructureChanged()
    {
        RebuildAllSlots();
        RefreshAllSlots();
    }
}
