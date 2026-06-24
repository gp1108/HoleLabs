using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks active runtime world objects that can affect performance or emergency cleanup decisions.
/// Objects register themselves when they become active and unregister when they are pooled, disabled or destroyed.
/// </summary>
[DisallowMultipleComponent]
public sealed class RuntimeWorldObjectRegistry : MonoBehaviour
{
    /// <summary>
    /// Global runtime instance used by pickups and world items to register without serialized scene wiring.
    /// </summary>
    public static RuntimeWorldObjectRegistry Instance { get; private set; }

    [Header("Startup")]
    [Tooltip("If true, the registry scans the active scene once during Awake to catch objects that were enabled before this service initialized.")]
    [SerializeField] private bool RebuildFromSceneOnAwake = true;

    [Header("Debug")]
    [Tooltip("Logs registration, unregistration and rebuild operations.")]
    [SerializeField] private bool DebugLogs = false;

    private readonly HashSet<OrePickup> ActiveOrePickups = new();
    private readonly HashSet<MoneyPickup> ActiveMoneyPickups = new();
    private readonly HashSet<WorldItem> ActiveWorldItems = new();

    /// <summary>
    /// Initializes singleton access and optionally rebuilds the registry from the current scene.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RuntimeWorldObjectRegistry] Multiple registries found. Keeping the first one.", this);
            return;
        }

        Instance = this;

        if (RebuildFromSceneOnAwake)
        {
            RebuildFromScene();
        }
    }

    /// <summary>
    /// Clears singleton access when this registry is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Registers an active ore pickup if a registry exists in the scene.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to register.</param>
    public static void RegisterOrePickup(OrePickup OrePickup)
    {
        if (OrePickup == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry Registry = ResolveInstance();

        if (Registry == null)
        {
            return;
        }

        Registry.RegisterOrePickupInternal(OrePickup);
    }

    /// <summary>
    /// Removes an ore pickup from the active registry if a registry exists in the scene.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to unregister.</param>
    public static void UnregisterOrePickup(OrePickup OrePickup)
    {
        if (OrePickup == null || Instance == null)
        {
            return;
        }

        Instance.UnregisterOrePickupInternal(OrePickup);
    }

    /// <summary>
    /// Registers an active money pickup if a registry exists in the scene.
    /// </summary>
    /// <param name="MoneyPickup">Money pickup to register.</param>
    public static void RegisterMoneyPickup(MoneyPickup MoneyPickup)
    {
        if (MoneyPickup == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry Registry = ResolveInstance();

        if (Registry == null)
        {
            return;
        }

        Registry.RegisterMoneyPickupInternal(MoneyPickup);
    }

    /// <summary>
    /// Removes a money pickup from the active registry if a registry exists in the scene.
    /// </summary>
    /// <param name="MoneyPickup">Money pickup to unregister.</param>
    public static void UnregisterMoneyPickup(MoneyPickup MoneyPickup)
    {
        if (MoneyPickup == null || Instance == null)
        {
            return;
        }

        Instance.UnregisterMoneyPickupInternal(MoneyPickup);
    }

    /// <summary>
    /// Registers an active world item if a registry exists in the scene.
    /// </summary>
    /// <param name="WorldItem">World item to register.</param>
    public static void RegisterWorldItem(WorldItem WorldItem)
    {
        if (WorldItem == null)
        {
            return;
        }

        RuntimeWorldObjectRegistry Registry = ResolveInstance();

        if (Registry == null)
        {
            return;
        }

        Registry.RegisterWorldItemInternal(WorldItem);
    }

    /// <summary>
    /// Removes a world item from the active registry if a registry exists in the scene.
    /// </summary>
    /// <param name="WorldItem">World item to unregister.</param>
    public static void UnregisterWorldItem(WorldItem WorldItem)
    {
        if (WorldItem == null || Instance == null)
        {
            return;
        }

        Instance.UnregisterWorldItemInternal(WorldItem);
    }

    /// <summary>
    /// Rebuilds all tracked object lists from currently active scene objects.
    /// Use this from emergency menus if you suspect the registry has stale data after manual scene edits.
    /// </summary>
    [ContextMenu("Rebuild From Scene")]
    public void RebuildFromScene()
    {
        ActiveOrePickups.Clear();
        ActiveMoneyPickups.Clear();
        ActiveWorldItems.Clear();

        OrePickup[] OrePickups = FindObjectsByType<OrePickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        MoneyPickup[] MoneyPickups = FindObjectsByType<MoneyPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        WorldItem[] WorldItems = FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int Index = 0; Index < OrePickups.Length; Index++)
        {
            RegisterOrePickupInternal(OrePickups[Index]);
        }

        for (int Index = 0; Index < MoneyPickups.Length; Index++)
        {
            RegisterMoneyPickupInternal(MoneyPickups[Index]);
        }

        for (int Index = 0; Index < WorldItems.Length; Index++)
        {
            RegisterWorldItemInternal(WorldItems[Index]);
        }

        Log("Rebuilt registry. Ores: " + GetActiveOreCount() + " | Money: " + GetActiveMoneyPickupCount() + " | World Items: " + GetActiveWorldItemCount());
    }

    /// <summary>
    /// Gets the amount of active ore pickups currently tracked.
    /// </summary>
    /// <returns>Active ore pickup count.</returns>
    public int GetActiveOreCount()
    {
        RemoveInvalidEntries(ActiveOrePickups);
        return ActiveOrePickups.Count;
    }

    /// <summary>
    /// Gets the amount of active money pickups currently tracked.
    /// </summary>
    /// <returns>Active money pickup count.</returns>
    public int GetActiveMoneyPickupCount()
    {
        RemoveInvalidEntries(ActiveMoneyPickups);
        return ActiveMoneyPickups.Count;
    }

    /// <summary>
    /// Gets the amount of active world items currently tracked.
    /// </summary>
    /// <returns>Active world item count.</returns>
    public int GetActiveWorldItemCount()
    {
        RemoveInvalidEntries(ActiveWorldItems);
        return ActiveWorldItems.Count;
    }

    /// <summary>
    /// Gets the amount of active world items considered runtime objects rather than original scene anchors.
    /// </summary>
    /// <returns>Active runtime world item count.</returns>
    public int GetActiveRuntimeWorldItemCount()
    {
        RemoveInvalidEntries(ActiveWorldItems);

        int Count = 0;

        foreach (WorldItem Item in ActiveWorldItems)
        {
            if (Item == null)
            {
                continue;
            }

            if (!IsScenePlacedWorldItem(Item))
            {
                Count++;
            }
        }

        return Count;
    }

    /// <summary>
    /// Gets the amount of active world items considered original scene-placed persistent objects.
    /// </summary>
    /// <returns>Active scene world item count.</returns>
    public int GetActiveSceneWorldItemCount()
    {
        RemoveInvalidEntries(ActiveWorldItems);

        int Count = 0;

        foreach (WorldItem Item in ActiveWorldItems)
        {
            if (Item == null)
            {
                continue;
            }

            if (IsScenePlacedWorldItem(Item))
            {
                Count++;
            }
        }

        return Count;
    }

    /// <summary>
    /// Gets a broad count of active physics-heavy gameplay objects tracked by this registry.
    /// </summary>
    /// <returns>Ores, money pickups and world items combined.</returns>
    public int GetTrackedPhysicsObjectCount()
    {
        return GetActiveOreCount() + GetActiveMoneyPickupCount() + GetActiveWorldItemCount();
    }

    /// <summary>
    /// Copies active ore pickups into a caller-owned list without allocating a new collection.
    /// </summary>
    /// <param name="Output">List receiving tracked ore pickups.</param>
    public void CopyActiveOrePickups(List<OrePickup> Output)
    {
        CopyValidEntries(ActiveOrePickups, Output);
    }

    /// <summary>
    /// Copies active money pickups into a caller-owned list without allocating a new collection.
    /// </summary>
    /// <param name="Output">List receiving tracked money pickups.</param>
    public void CopyActiveMoneyPickups(List<MoneyPickup> Output)
    {
        CopyValidEntries(ActiveMoneyPickups, Output);
    }

    /// <summary>
    /// Copies active world items into a caller-owned list without allocating a new collection.
    /// </summary>
    /// <param name="Output">List receiving tracked world items.</param>
    public void CopyActiveWorldItems(List<WorldItem> Output)
    {
        CopyValidEntries(ActiveWorldItems, Output);
    }

    /// <summary>
    /// Gets whether a world item belongs to a valid original scene-placed persistence wrapper.
    /// </summary>
    /// <param name="Item">World item to classify.</param>
    /// <returns>True when the item should be treated as an original scene object.</returns>
    public bool IsScenePlacedWorldItem(WorldItem Item)
    {
        if (Item == null)
        {
            return false;
        }

        ScenePlacedWorldItemPersistence Persistence = Item.GetComponentInParent<ScenePlacedWorldItemPersistence>(true);
        return Persistence != null && Persistence.ShouldPreserveAsScenePlacedItem();
    }

    /// <summary>
    /// Logs current tracked counts to the Unity console.
    /// </summary>
    [ContextMenu("Debug Counts")]
    public void DebugCounts()
    {
        Debug.Log(
            "[RuntimeWorldObjectRegistry] Ores: " + GetActiveOreCount() +
            " | Money: " + GetActiveMoneyPickupCount() +
            " | Runtime World Items: " + GetActiveRuntimeWorldItemCount() +
            " | Scene World Items: " + GetActiveSceneWorldItemCount() +
            " | Total: " + GetTrackedPhysicsObjectCount(),
            this);
    }

    /// <summary>
    /// Resolves the current registry instance without creating one implicitly.
    /// </summary>
    /// <returns>Runtime registry instance or null.</returns>
    private static RuntimeWorldObjectRegistry ResolveInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        Instance = FindFirstObjectByType<RuntimeWorldObjectRegistry>();
        return Instance;
    }

    /// <summary>
    /// Registers an ore pickup in the internal active set.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to register.</param>
    private void RegisterOrePickupInternal(OrePickup OrePickup)
    {
        if (OrePickup == null || !OrePickup.gameObject.activeInHierarchy || OrePickup.GetOreItemData() == null)
        {
            return;
        }

        ActiveOrePickups.Add(OrePickup);
    }

    /// <summary>
    /// Removes an ore pickup from the internal active set.
    /// </summary>
    /// <param name="OrePickup">Ore pickup to unregister.</param>
    private void UnregisterOrePickupInternal(OrePickup OrePickup)
    {
        ActiveOrePickups.Remove(OrePickup);
    }

    /// <summary>
    /// Registers a money pickup in the internal active set.
    /// </summary>
    /// <param name="MoneyPickup">Money pickup to register.</param>
    private void RegisterMoneyPickupInternal(MoneyPickup MoneyPickup)
    {
        if (MoneyPickup == null || !MoneyPickup.gameObject.activeInHierarchy || MoneyPickup.GetAmount() <= 0f)
        {
            return;
        }

        ActiveMoneyPickups.Add(MoneyPickup);
    }

    /// <summary>
    /// Removes a money pickup from the internal active set.
    /// </summary>
    /// <param name="MoneyPickup">Money pickup to unregister.</param>
    private void UnregisterMoneyPickupInternal(MoneyPickup MoneyPickup)
    {
        ActiveMoneyPickups.Remove(MoneyPickup);
    }

    /// <summary>
    /// Registers a world item in the internal active set.
    /// </summary>
    /// <param name="WorldItem">World item to register.</param>
    private void RegisterWorldItemInternal(WorldItem WorldItem)
    {
        if (WorldItem == null || !WorldItem.gameObject.activeInHierarchy)
        {
            return;
        }

        ActiveWorldItems.Add(WorldItem);
    }

    /// <summary>
    /// Removes a world item from the internal active set.
    /// </summary>
    /// <param name="WorldItem">World item to unregister.</param>
    private void UnregisterWorldItemInternal(WorldItem WorldItem)
    {
        ActiveWorldItems.Remove(WorldItem);
    }

    /// <summary>
    /// Copies valid entries from a tracked hash set into a caller-owned list.
    /// </summary>
    private static void CopyValidEntries<T>(HashSet<T> Source, List<T> Output) where T : Object
    {
        if (Output == null)
        {
            return;
        }

        Output.Clear();

        foreach (T Entry in Source)
        {
            if (Entry == null)
            {
                continue;
            }

            Output.Add(Entry);
        }
    }

    /// <summary>
    /// Removes destroyed Unity objects from a tracked hash set.
    /// </summary>
    private static void RemoveInvalidEntries<T>(HashSet<T> Source) where T : Object
    {
        if (Source == null || Source.Count == 0)
        {
            return;
        }

        List<T> InvalidEntries = null;

        foreach (T Entry in Source)
        {
            if (Entry != null)
            {
                continue;
            }

            InvalidEntries ??= new List<T>();
            InvalidEntries.Add(Entry);
        }

        if (InvalidEntries == null)
        {
            return;
        }

        for (int Index = 0; Index < InvalidEntries.Count; Index++)
        {
            Source.Remove(InvalidEntries[Index]);
        }
    }

    /// <summary>
    /// Logs registry messages if debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[RuntimeWorldObjectRegistry] " + Message, this);
    }
}
