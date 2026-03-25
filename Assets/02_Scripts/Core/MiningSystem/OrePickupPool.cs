using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central pool responsible for reusing dropped ore pickup instances.
/// The pool is keyed by prefab so each ore type can reuse its own object variant safely.
/// </summary>
public sealed class OrePickupPool : MonoBehaviour
{
    [Serializable]
    private sealed class PrewarmEntry
    {
        [Tooltip("Dropped ore prefab that should be prewarmed in the pool.")]
        [SerializeField] private GameObject Prefab;

        [Tooltip("Amount of inactive instances created on Awake for this prefab.")]
        [SerializeField] private int Count;

        /// <summary>
        /// Gets the configured pickup prefab.
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

    private readonly Dictionary<GameObject, Queue<OrePickup>> AvailablePickupsByPrefab = new();

    /// <summary>
    /// Prewarms configured pickup prefabs.
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
    /// Gets an available pickup instance for the provided prefab.
    /// Creates a new one only when the pool is empty.
    /// </summary>
    public OrePickup GetPickup(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            return null;
        }

        Queue<OrePickup> availablePickups = GetOrCreateQueue(prefab);
        OrePickup pickup = null;

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
        Log("Reused pickup for prefab: " + prefab.name);
        return pickup;
    }

    /// <summary>
    /// Returns a pickup instance back to the pool associated with the provided prefab.
    /// </summary>
    public void ReturnPickup(OrePickup pickup, GameObject prefab)
    {
        if (pickup == null || prefab == null)
        {
            return;
        }

        Queue<OrePickup> availablePickups = GetOrCreateQueue(prefab);
        pickup.PrepareForPoolStorage(PoolRoot);
        availablePickups.Enqueue(pickup);
        Log("Returned pickup to pool for prefab: " + prefab.name);
    }

    /// <summary>
    /// Creates one inactive instance and stores it immediately in the pool.
    /// </summary>
    private void CreateAndStoreInstance(GameObject prefab)
    {
        OrePickup pickup = CreateInstance(prefab);

        if (pickup == null)
        {
            return;
        }

        ReturnPickup(pickup, prefab);
    }

    /// <summary>
    /// Creates a new pickup instance from the provided prefab and binds it to this pool.
    /// </summary>
    private OrePickup CreateInstance(GameObject prefab)
    {
        GameObject instance = Instantiate(prefab, PoolRoot);
        OrePickup pickup = instance.GetComponent<OrePickup>();

        if (pickup == null)
        {
            pickup = instance.GetComponentInChildren<OrePickup>(true);
        }

        if (pickup == null)
        {
            Debug.LogError("[OrePickupPool] Missing OrePickup component on prefab: " + prefab.name, this);
            Destroy(instance);
            return null;
        }

        pickup.BindPool(this, prefab);
        return pickup;
    }

    /// <summary>
    /// Ensures one queue exists for the provided prefab.
    /// </summary>
    private Queue<OrePickup> GetOrCreateQueue(GameObject prefab)
    {
        if (!AvailablePickupsByPrefab.TryGetValue(prefab, out Queue<OrePickup> queue))
        {
            queue = new Queue<OrePickup>();
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

        GameObject poolRootObject = new GameObject("OrePickupPoolRoot");
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

        Debug.Log("[OrePickupPool] " + message, this);
    }
}
