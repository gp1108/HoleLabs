using UnityEngine;

/// <summary>
/// Physical collectible currency emitted by the ore selling machine.
/// The wallet is credited only when a collector explicitly picks this object up through interaction.
/// </summary>
public sealed class MoneyPickup : MonoBehaviour
{
    [Header("Runtime Data")]
    [Tooltip("Currency type granted when this pickup is collected.")]
    [SerializeField] private CurrencyWallet.CurrencyType CurrencyType = CurrencyWallet.CurrencyType.Gold;

    [Tooltip("Amount granted when this pickup is collected.")]
    [SerializeField] private int Amount = 1;

    [Header("Structure")]
    [Tooltip("Root transform moved, activated and deactivated by the pool. If empty, this transform is used.")]
    [SerializeField] private Transform RuntimeRoot;

    [Header("Cached Components")]
    [Tooltip("Optional rigidbody reset when the pickup is reused by the pool.")]
    [SerializeField] private Rigidbody CachedRigidbody;

    [Tooltip("Optional collider array enabled again when the pickup is reused by the pool.")]
    [SerializeField] private Collider[] CachedColliders;

    private MoneyPickupPool OwnerPool;
    private GameObject SourcePrefab;

    /// <summary>
    /// Initializes the runtime currency payload stored by this pickup.
    /// </summary>
    public void Initialize(int amount, CurrencyWallet.CurrencyType currencyType)
    {
        Amount = Mathf.Max(1, amount);
        CurrencyType = currencyType;
        GetRuntimeRoot().name = "MoneyPickup_" + CurrencyType + "_" + Amount;
    }

    /// <summary>
    /// Binds pool ownership data used when the pickup is later returned.
    /// </summary>
    public void BindPool(MoneyPickupPool ownerPool, GameObject sourcePrefab)
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
        Amount = 0;
        runtimeRoot.name = SourcePrefab != null ? SourcePrefab.name + "_Pooled" : "MoneyPickup_Pooled";
        runtimeRoot.SetParent(poolRoot, false);
        runtimeRoot.gameObject.SetActive(false);
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
    /// Gets the configured currency type granted on collection.
    /// </summary>
    public CurrencyWallet.CurrencyType GetCurrencyType()
    {
        return CurrencyType;
    }

    /// <summary>
    /// Gets the amount granted on collection.
    /// </summary>
    public int GetAmount()
    {
        return Mathf.Max(0, Amount);
    }

    /// <summary>
    /// Gets the cached rigidbody used for emission impulses.
    /// </summary>
    public Rigidbody GetCachedRigidbody()
    {
        EnsureCachedReferences();
        return CachedRigidbody;
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
    /// Resets linear and angular rigidbody velocity before reusing or storing the pickup.
    /// </summary>
    private void ResetPhysicsState()
    {
        if (CachedRigidbody == null)
        {
            return;
        }

        CachedRigidbody.linearVelocity = Vector3.zero;
        CachedRigidbody.angularVelocity = Vector3.zero;
        CachedRigidbody.Sleep();
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
