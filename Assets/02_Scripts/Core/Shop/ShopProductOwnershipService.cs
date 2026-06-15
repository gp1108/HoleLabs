using UnityEngine;

/// <summary>
/// Resolves runtime ownership for shop products that are limited by physical existence.
/// This service derives state from live world items, player hotbars and installed placeable spots instead of storing separate ownership data.
/// </summary>
public sealed class ShopProductOwnershipService : MonoBehaviour
{
    /// <summary>
    /// Runtime ownership state for a shop product.
    /// </summary>
    public enum ProductOwnershipState
    {
        NotTracked = 0,
        NotOwned = 1,
        LooseInstance = 2,
        Installed = 3
    }

    [Header("Search")]
    [Tooltip("If true, inactive objects are also scanned. Leave disabled unless unique product instances are intentionally kept inactive but still owned.")]
    [SerializeField] private bool IncludeInactiveObjects = false;

    [Header("Debug")]
    [Tooltip("Logs ownership scans and removals.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Gets the current ownership state for the provided product.
    /// </summary>
    /// <param name="ProductDefinition">Product to evaluate.</param>
    /// <returns>Derived runtime ownership state.</returns>
    public ProductOwnershipState GetOwnershipState(ShopProductDefinition ProductDefinition)
    {
        if (!IsTrackableUniqueProduct(ProductDefinition))
        {
            return ProductOwnershipState.NotTracked;
        }

        ItemDefinition DeliveredItemDefinition = ProductDefinition.GetDeliveredItemDefinition();

        if (HasInstalledInstance(DeliveredItemDefinition))
        {
            return ProductOwnershipState.Installed;
        }

        if (HasLooseInstance(DeliveredItemDefinition))
        {
            return ProductOwnershipState.LooseInstance;
        }

        return ProductOwnershipState.NotOwned;
    }

    /// <summary>
    /// Gets whether the provided product currently has an installed unique instance.
    /// </summary>
    /// <param name="ProductDefinition">Product to evaluate.</param>
    /// <returns>True when an installed instance exists.</returns>
    public bool IsInstalled(ShopProductDefinition ProductDefinition)
    {
        return GetOwnershipState(ProductDefinition) == ProductOwnershipState.Installed;
    }

    /// <summary>
    /// Gets whether the provided product currently has a loose world or hotbar instance.
    /// </summary>
    /// <param name="ProductDefinition">Product to evaluate.</param>
    /// <returns>True when a loose instance exists.</returns>
    public bool HasLooseInstance(ShopProductDefinition ProductDefinition)
    {
        return GetOwnershipState(ProductDefinition) == ProductOwnershipState.LooseInstance;
    }

    /// <summary>
    /// Removes every loose live instance of the delivered item from hotbars and the physical world.
    /// Installed instances are never removed by this method.
    /// </summary>
    /// <param name="ProductDefinition">Unique product whose loose instances should be removed.</param>
    /// <returns>Number of loose containers removed.</returns>
    public int RemoveLooseInstances(ShopProductDefinition ProductDefinition)
    {
        if (!IsTrackableUniqueProduct(ProductDefinition))
        {
            return 0;
        }

        ItemDefinition DeliveredItemDefinition = ProductDefinition.GetDeliveredItemDefinition();
        int RemovedCount = RemoveLooseHotbarInstances(DeliveredItemDefinition);
        RemovedCount += RemoveLooseWorldInstances(DeliveredItemDefinition);

        Log("Removed " + RemovedCount + " loose instance(s) for unique product group: " + ProductDefinition.GetUniqueGroupId());
        return RemovedCount;
    }

    /// <summary>
    /// Returns whether this product has the data required for ownership tracking.
    /// </summary>
    /// <param name="ProductDefinition">Product to evaluate.</param>
    /// <returns>True when unique ownership can be resolved.</returns>
    public bool IsTrackableUniqueProduct(ShopProductDefinition ProductDefinition)
    {
        return ProductDefinition != null &&
               ProductDefinition.UsesUniqueReissueStock() &&
               ProductDefinition.ShouldSpawnWorldItem() &&
               ProductDefinition.GetDeliveredItemDefinition() != null;
    }

    /// <summary>
    /// Gets whether an installed placeable instance exists for the delivered item definition.
    /// </summary>
    /// <param name="DeliveredItemDefinition">Item definition delivered by the unique product.</param>
    /// <returns>True when the item is installed in any placement spot.</returns>
    private bool HasInstalledInstance(ItemDefinition DeliveredItemDefinition)
    {
        if (DeliveredItemDefinition == null)
        {
            return false;
        }

        PlaceableInstallationSpot[] Spots = FindObjectsByType<PlaceableInstallationSpot>(GetInactiveSearchMode(), FindObjectsSortMode.None);

        for (int Index = 0; Index < Spots.Length; Index++)
        {
            PlaceableInstallationSpot Spot = Spots[Index];

            if (Spot == null || !Spot.GetIsOccupied())
            {
                continue;
            }

            PlaceableItemDefinition InstalledDefinition = Spot.GetCurrentInstalledPlaceableDefinition();

            if (InstalledDefinition == DeliveredItemDefinition)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets whether a loose world or hotbar instance exists for the delivered item definition.
    /// </summary>
    /// <param name="DeliveredItemDefinition">Item definition delivered by the unique product.</param>
    /// <returns>True when a loose instance exists.</returns>
    private bool HasLooseInstance(ItemDefinition DeliveredItemDefinition)
    {
        return HasLooseHotbarInstance(DeliveredItemDefinition) || HasLooseWorldInstance(DeliveredItemDefinition);
    }

    /// <summary>
    /// Gets whether any player hotbar contains the delivered item definition.
    /// </summary>
    /// <param name="DeliveredItemDefinition">Item definition to search for.</param>
    /// <returns>True when found in any hotbar slot.</returns>
    private bool HasLooseHotbarInstance(ItemDefinition DeliveredItemDefinition)
    {
        if (DeliveredItemDefinition == null)
        {
            return false;
        }

        HotbarController[] Hotbars = FindObjectsByType<HotbarController>(GetInactiveSearchMode(), FindObjectsSortMode.None);

        for (int HotbarIndex = 0; HotbarIndex < Hotbars.Length; HotbarIndex++)
        {
            HotbarController Hotbar = Hotbars[HotbarIndex];

            if (Hotbar == null)
            {
                continue;
            }

            int SlotCount = Hotbar.GetSlotCount();

            for (int SlotIndex = 0; SlotIndex < SlotCount; SlotIndex++)
            {
                ItemInstance ItemInstance = Hotbar.GetItemAtSlot(SlotIndex);

                if (ItemInstance != null && ItemInstance.GetDefinition() == DeliveredItemDefinition)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets whether the physical world contains a loose item matching the delivered definition.
    /// </summary>
    /// <param name="DeliveredItemDefinition">Item definition to search for.</param>
    /// <returns>True when found in the world outside installed placeable visuals.</returns>
    private bool HasLooseWorldInstance(ItemDefinition DeliveredItemDefinition)
    {
        if (DeliveredItemDefinition == null)
        {
            return false;
        }

        WorldItem[] WorldItems = FindObjectsByType<WorldItem>(GetInactiveSearchMode(), FindObjectsSortMode.None);

        for (int Index = 0; Index < WorldItems.Length; Index++)
        {
            WorldItem WorldItem = WorldItems[Index];

            if (!IsLooseMatchingWorldItem(WorldItem, DeliveredItemDefinition))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes matching item instances from every live hotbar.
    /// </summary>
    /// <param name="DeliveredItemDefinition">Item definition to remove.</param>
    /// <returns>Number of hotbar slots cleared.</returns>
    private int RemoveLooseHotbarInstances(ItemDefinition DeliveredItemDefinition)
    {
        if (DeliveredItemDefinition == null)
        {
            return 0;
        }

        int RemovedCount = 0;
        HotbarController[] Hotbars = FindObjectsByType<HotbarController>(GetInactiveSearchMode(), FindObjectsSortMode.None);

        for (int Index = 0; Index < Hotbars.Length; Index++)
        {
            if (Hotbars[Index] == null)
            {
                continue;
            }

            RemovedCount += Hotbars[Index].RemoveItemsByDefinition(DeliveredItemDefinition);
        }

        return RemovedCount;
    }

    /// <summary>
    /// Removes matching loose physical world items.
    /// Scene-persistent items are marked absent while runtime-spawned items are destroyed.
    /// </summary>
    /// <param name="DeliveredItemDefinition">Item definition to remove.</param>
    /// <returns>Number of world item containers removed.</returns>
    private int RemoveLooseWorldInstances(ItemDefinition DeliveredItemDefinition)
    {
        if (DeliveredItemDefinition == null)
        {
            return 0;
        }

        int RemovedCount = 0;
        WorldItem[] WorldItems = FindObjectsByType<WorldItem>(GetInactiveSearchMode(), FindObjectsSortMode.None);

        for (int Index = 0; Index < WorldItems.Length; Index++)
        {
            WorldItem WorldItem = WorldItems[Index];

            if (!IsLooseMatchingWorldItem(WorldItem, DeliveredItemDefinition))
            {
                continue;
            }

            ScenePlacedWorldItemPersistence ScenePersistence = WorldItem.GetComponentInParent<ScenePlacedWorldItemPersistence>();

            if (ScenePersistence != null)
            {
                ScenePersistence.SetPresent(false);
            }
            else
            {
                Destroy(WorldItem.gameObject);
            }

            RemovedCount++;
        }

        return RemovedCount;
    }

    /// <summary>
    /// Returns whether this world item is a loose match for the delivered item definition.
    /// Installed visuals are intentionally ignored so installed products are never destroyed by reissue.
    /// </summary>
    /// <param name="WorldItem">World item to evaluate.</param>
    /// <param name="DeliveredItemDefinition">Item definition that must match.</param>
    /// <returns>True when this world item is a removable loose instance.</returns>
    private bool IsLooseMatchingWorldItem(WorldItem WorldItem, ItemDefinition DeliveredItemDefinition)
    {
        if (WorldItem == null || DeliveredItemDefinition == null)
        {
            return false;
        }

        if (WorldItem.GetDefinition() != DeliveredItemDefinition)
        {
            return false;
        }

        if (WorldItem.GetComponentInParent<PlaceableInstallationSpot>() != null)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the Unity inactive-object search mode from the inspector setting.
    /// </summary>
    /// <returns>Inactive-object search mode used by runtime scans.</returns>
    private FindObjectsInactive GetInactiveSearchMode()
    {
        return IncludeInactiveObjects ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
    }

    /// <summary>
    /// Logs ownership messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ShopProductOwnershipService] " + Message, this);
    }
}
