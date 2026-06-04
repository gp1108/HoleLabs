using UnityEngine;

/// <summary>
/// Defines the weight contributed by the player while inside an elevator.
/// It can include the player's base body weight and optional hotbar item weight.
/// </summary>
[DisallowMultipleComponent]
public sealed class ElevatorWeightActor : MonoBehaviour
{
    [Header("Base Weight")]
    [Tooltip("Base body weight contributed while this actor is inside the elevator.")]
    [SerializeField] private float BaseWeight = 0f;

    [Header("Hotbar Weight")]
    [Tooltip("If true, inventory items stored in the hotbar also contribute to elevator weight while this actor is inside the elevator.")]
    [SerializeField] private bool IncludeHotbarWeight = true;

    [Tooltip("Hotbar used to evaluate carried item weight. If empty, one is searched in this hierarchy.")]
    [SerializeField] private HotbarController HotbarController;

    /// <summary>
    /// Caches optional references.
    /// </summary>
    private void Awake()
    {
        if (HotbarController == null)
        {
            HotbarController = GetComponent<HotbarController>();
        }

        if (HotbarController == null)
        {
            HotbarController = GetComponentInChildren<HotbarController>(true);
        }
    }

    /// <summary>
    /// Gets the full actor weight currently contributed to the elevator.
    /// </summary>
    public float GetBaseWeight()
    {
        return Mathf.Max(0f, BaseWeight) + GetHotbarWeight();
    }

    /// <summary>
    /// Gets the current weight of every item stored in the player's hotbar.
    /// </summary>
    /// <returns>Non-negative hotbar weight.</returns>
    private float GetHotbarWeight()
    {
        if (!IncludeHotbarWeight || HotbarController == null)
        {
            return 0f;
        }

        float TotalWeight = 0f;
        int SlotCount = HotbarController.GetSlotCount();

        for (int SlotIndex = 0; SlotIndex < SlotCount; SlotIndex++)
        {
            ItemInstance ItemInstance = HotbarController.GetItemAtSlot(SlotIndex);

            if (ItemInstance == null || ItemInstance.GetDefinition() == null)
            {
                continue;
            }

            float ItemWeight = ItemInstance.GetDefinition().GetBaseWeight();
            int Amount = Mathf.Max(1, ItemInstance.GetAmount());
            TotalWeight += Mathf.Max(0f, ItemWeight) * Amount;
        }

        return Mathf.Max(0f, TotalWeight);
    }
}
