using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central pool responsible for reusing physical money pickup instances.
/// The pool is keyed by prefab so coins and bills can keep different visuals and rigidbodies safely.
/// </summary>
public sealed class MoneyPickupPool : MonoBehaviour
{
    [Serializable]
    private sealed class PrewarmEntry
    {
        [Tooltip("Money prefab that should be prewarmed in the pool.")]
        [SerializeField] private GameObject Prefab;

        [Tooltip("Amount of inactive instances created on Awake for this prefab.")]
        [SerializeField] private int Count;

        /// <summary>
        /// Gets the configured money prefab.
        /// </summary>
        public GameObject GetPrefab()
        {
            return Prefab;
        }

        /// <summary>
        /// Gets the configured prewarm count.
        /// </summary>
        public int GetCount()
        {
            return Mathf.Max(0, Count);
        }
    }

    [Header("Prewarm")]
    [Tooltip("Optional list of prefabs to prewarm during Awake.")]
    [SerializeField] private List<PrewarmEntry> PrewarmEntries = new();

    [Header("Hierarchy")]
    [Tooltip("Optional parent used to store pooled inactive objects.")]
    [SerializeField] private Transform PoolRoot;

    [Header("Debug")]
    [Tooltip("Logs pool get and return operations.")]
    [SerializeField] private bool DebugLogs = false;

    private readonly Dictionary<GameObject, Queue<MoneyPickup>> AvailablePickupsByPrefab = new();

    /// <summary>
    /// Prewarms configured money prefabs.
    /// </summary>
    private void Awake()
    {
        EnsurePoolRoot();

        for (int index = 0; index < PrewarmEntries.Count; index++)
        {
            PrewarmEntry entry = PrewarmEntries[index];

            if (entry == null || entry.GetPrefab() == null)
            {
                continue;
            }

            for (int spawnIndex = 0; spawnIndex < entry.GetCount(); spawnIndex++)
            {
                CreateAndStoreInstance(entry.GetPrefab());
            }
        }
    }

    /// <summary>
    /// Gets an available money pickup instance for the provided prefab.
    /// Creates a new one only when the pool is empty.
    /// </summary>
    public MoneyPickup GetPickup(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            return null;
        }

        Queue<MoneyPickup> availablePickups = GetOrCreateQueue(prefab);
        MoneyPickup pickup = null;

        while (availablePickups.Count > 0 && pickup == null)
        {
            pickup = availablePickups.Dequeue();
        }

        if (pickup == null)
        {
            pickup = CreateInstance(prefab);
        }

        if (pickup == null)
        {
            return null;
        }

        pickup.PrepareForReuse(position, rotation);
        Log("Reused money pickup for prefab: " + prefab.name);
        return pickup;
    }

    /// <summary>
    /// Returns a money pickup instance back to the pool associated with the provided prefab.
    /// </summary>
    public void ReturnPickup(MoneyPickup pickup, GameObject prefab)
    {
        if (pickup == null || prefab == null)
        {
            return;
        }

        Queue<MoneyPickup> availablePickups = GetOrCreateQueue(prefab);
        pickup.PrepareForPoolStorage(PoolRoot);
        availablePickups.Enqueue(pickup);
        Log("Returned money pickup to pool for prefab: " + prefab.name);
    }

    /// <summary>
    /// Creates one inactive instance and stores it immediately in the pool.
    /// </summary>
    private void CreateAndStoreInstance(GameObject prefab)
    {
        MoneyPickup pickup = CreateInstance(prefab);

        if (pickup == null)
        {
            return;
        }

        ReturnPickup(pickup, prefab);
    }

    /// <summary>
    /// Creates a new money pickup instance from the provided prefab and binds it to this pool.
    /// </summary>
    private MoneyPickup CreateInstance(GameObject prefab)
    {
        GameObject instance = Instantiate(prefab, PoolRoot);
        MoneyPickup pickup = instance.GetComponent<MoneyPickup>();

        if (pickup == null)
        {
            pickup = instance.GetComponentInChildren<MoneyPickup>(true);
        }

        if (pickup == null)
        {
            Debug.LogError("[MoneyPickupPool] Missing MoneyPickup component on prefab: " + prefab.name, this);
            Destroy(instance);
            return null;
        }

        pickup.BindPool(this, prefab);
        return pickup;
    }

    /// <summary>
    /// Ensures one queue exists for the provided prefab.
    /// </summary>
    private Queue<MoneyPickup> GetOrCreateQueue(GameObject prefab)
    {
        if (!AvailablePickupsByPrefab.TryGetValue(prefab, out Queue<MoneyPickup> queue))
        {
            queue = new Queue<MoneyPickup>();
            AvailablePickupsByPrefab.Add(prefab, queue);
        }

        return queue;
    }

    /// <summary>
    /// Ensures the pool has a dedicated transform root for inactive instances.
    /// </summary>
    private void EnsurePoolRoot()
    {
        if (PoolRoot != null)
        {
            return;
        }

        GameObject poolRootObject = new GameObject("MoneyPickupPoolRoot");
        poolRootObject.transform.SetParent(transform, false);
        PoolRoot = poolRootObject.transform;
    }

    /// <summary>
    /// Logs pool messages if debug logging is enabled.
    /// </summary>
    private void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[MoneyPickupPool] " + message, this);
    }
}
