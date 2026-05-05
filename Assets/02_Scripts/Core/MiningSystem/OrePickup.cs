using UnityEngine;

/// <summary>
/// Stores runtime ore data on a dropped physical ore object.
/// This component is separate from the player's generic item system so ore-specific
/// properties can remain flexible without polluting every item type.
/// </summary>
public sealed class OrePickup : MonoBehaviour
{
    [Header("Runtime Data")]
    [Tooltip("Runtime ore data carried by this dropped pickup.")]
    [SerializeField] private OreItemData OreItemData;

    [Header("Structure")]
    [Tooltip("Root transform moved, activated and deactivated by the pool. If empty, this transform is used.")]
    [SerializeField] private Transform RuntimeRoot;

    [Header("Cached Components")]
    [Tooltip("Optional rigidbody reset when the pickup is reused by the pool.")]
    [SerializeField] private Rigidbody CachedRigidbody;

    [Tooltip("Optional collider array enabled again when the pickup is reused by the pool.")]
    [SerializeField] private Collider[] CachedColliders;

    private OrePickupPool OwnerPool;
    private GameObject SourcePrefab;

    /// <summary>
    /// Gets the prefab originally used to create this pickup.
    /// This is used by the save system to recreate the same visual object.
    /// </summary>
    public GameObject GetSourcePrefab()
    {
        return SourcePrefab;
    }

    /// <summary>
    /// Gets the source prefab name used to recreate the same ore pickup visual during load.
    /// </summary>
    public string GetSourcePrefabName()
    {
        return SourcePrefab != null ? SourcePrefab.name : string.Empty;
    }

    /// <summary>
    /// Initializes this pickup with runtime ore data.
    /// </summary>
    public void Initialize(OreItemData oreItemData)
    {
        OreItemData = oreItemData;

        if (OreItemData != null && OreItemData.GetOreDefinition() != null)
        {
            GetRuntimeRoot().name = "OrePickup_" + OreItemData.GetOreDefinition().GetDisplayName();
        }
    }

    /// <summary>
    /// Binds pool ownership data used when the pickup is returned.
    /// </summary>
    public void BindPool(OrePickupPool ownerPool, GameObject sourcePrefab)
    {
        OwnerPool = ownerPool;
        SourcePrefab = sourcePrefab;
    }

    /// <summary>
    /// Prepares the pickup to be reused at the provided world transform.
    /// </summary>
    public void PrepareForReuse(Vector3 position, Quaternion rotation)
    {
        Transform runtimeRoot = GetRuntimeRoot();
        runtimeRoot.SetParent(null, true);
        runtimeRoot.SetPositionAndRotation(position, rotation);

        EnsureCachedReferences();
        ResetPhysicsState();
        SetCollidersEnabled(true);
        runtimeRoot.gameObject.SetActive(true);
    }

    /// <summary>
    /// Prepares the pickup to be stored back inside the pool.
    /// </summary>
    public void PrepareForPoolStorage(Transform poolRoot)
    {
        Transform runtimeRoot = GetRuntimeRoot();

        EnsureCachedReferences();
        ResetPhysicsState();
        SetCollidersEnabled(false);
        OreItemData = null;
        runtimeRoot.name = SourcePrefab != null ? SourcePrefab.name + "_Pooled" : "OrePickup_Pooled";

        SetContainedCarryablesDisableResetSuppressed(true);
        runtimeRoot.SetParent(poolRoot, false);
        runtimeRoot.gameObject.SetActive(false);
        SetContainedCarryablesDisableResetSuppressed(false);
    }

    /// <summary>
    /// Attempts to return this pickup back to its owner pool.
    /// </summary>
    public bool ReturnToPool()
    {
        if (OwnerPool == null || SourcePrefab == null)
        {
            return false;
        }

        OwnerPool.ReturnPickup(this, SourcePrefab);
        return true;
    }

    /// <summary>
    /// Gets the runtime ore payload currently stored by this pickup.
    /// </summary>
    public OreItemData GetOreItemData()
    {
        return OreItemData;
    }

    /// <summary>
    /// Gets the root transform controlled by the pool.
    /// </summary>
    public Transform GetRuntimeRoot()
    {
        if (RuntimeRoot == null)
        {
            RuntimeRoot = transform;
        }

        return RuntimeRoot;
    }

    /// <summary>
    /// Resets rigidbody motion before reusing or storing the pickup.
    /// Kinematic rigidbodies cannot accept velocity writes, so only dynamic bodies are zeroed explicitly.
    /// </summary>
    private void ResetPhysicsState()
    {
        if (CachedRigidbody == null)
        {
            return;
        }

        if (!CachedRigidbody.isKinematic)
        {
            CachedRigidbody.linearVelocity = Vector3.zero;
            CachedRigidbody.angularVelocity = Vector3.zero;
        }

        CachedRigidbody.Sleep();
    }

    /// <summary>
    /// Suppresses or restores disable reset on every physics carryable in this pickup hierarchy.
    /// </summary>
    /// <param name="IsSuppressed">True to suppress disable reset, false to restore it.</param>
    private void SetContainedCarryablesDisableResetSuppressed(bool IsSuppressed)
    {
        PhysicsCarryable[] Carryables = GetComponentsInChildren<PhysicsCarryable>(true);

        for (int Index = 0; Index < Carryables.Length; Index++)
        {
            if (Carryables[Index] == null)
            {
                continue;
            }

            Carryables[Index].SetDisableResetSuppressed(IsSuppressed);
        }
    }

    /// <summary>
    /// Enables or disables every cached collider during pool transitions.
    /// </summary>
    private void SetCollidersEnabled(bool isEnabled)
    {
        if (CachedColliders == null)
        {
            return;
        }

        for (int index = 0; index < CachedColliders.Length; index++)
        {
            if (CachedColliders[index] == null)
            {
                continue;
            }

            CachedColliders[index].enabled = isEnabled;
        }
    }

    /// <summary>
    /// Caches missing rigidbody and collider references the first time they are needed.
    /// </summary>
    private void EnsureCachedReferences()
    {
        if (CachedRigidbody == null)
        {
            CachedRigidbody = GetComponent<Rigidbody>();

            if (CachedRigidbody == null)
            {
                CachedRigidbody = GetComponentInChildren<Rigidbody>(true);
            }
        }

        if (CachedColliders == null || CachedColliders.Length == 0)
        {
            CachedColliders = GetComponentsInChildren<Collider>(true);
        }
    }
}
