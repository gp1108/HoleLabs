using UnityEngine;

/// <summary>
/// Physical collectible currency emitted by the ore selling machine.
/// The wallet is credited only when a collector explicitly picks this object up through interaction.
/// </summary>
public sealed class MoneyPickup : MonoBehaviour
{
    [Header("Runtime Data")]
    [SerializeField] private CurrencyWallet.CurrencyType CurrencyType = CurrencyWallet.CurrencyType.Gold;
    [SerializeField] private float Amount = 0.01f;

    [Header("Structure")]
    [SerializeField] private Transform RuntimeRoot;

    [Header("Cached Components")]
    [SerializeField] private Rigidbody CachedRigidbody;
    [SerializeField] private Collider[] CachedColliders;

    [Tooltip("Optional explicit sleep controller used to reduce physics cost when money remains still on the floor.")]
    [SerializeField] private MoneyPickupSleepController SleepController;

    private MoneyPickupPool OwnerPool;
    private GameObject SourcePrefab;

    /// <summary>
    /// Initializes the runtime currency payload stored by this pickup.
    /// </summary>
    public void Initialize(float AmountValue, CurrencyWallet.CurrencyType CurrencyTypeValue)
    {
        Amount = CurrencyMath.RoundCurrency(Mathf.Max(0.01f, AmountValue));
        CurrencyType = CurrencyTypeValue;
        GetRuntimeRoot().name = "MoneyPickup_" + CurrencyType + "_" + Amount.ToString("0.00");
    }

    /// <summary>
    /// Binds pool ownership data used when the pickup is later returned.
    /// </summary>
    public void BindPool(MoneyPickupPool OwnerPoolValue, GameObject SourcePrefabValue)
    {
        OwnerPool = OwnerPoolValue;
        SourcePrefab = SourcePrefabValue;
    }

    /// <summary>
    /// Prepares the pickup to be reused at the provided world transform.
    /// </summary>
    public void PrepareForReuse(Vector3 Position, Quaternion Rotation)
    {
        Transform RuntimeRootTransform = GetRuntimeRoot();
        RuntimeRootTransform.SetParent(null, true);
        RuntimeRootTransform.SetPositionAndRotation(Position, Rotation);

        EnsureCachedReferences();
        ResetPhysicsState();
        SetCollidersEnabled(true);
        RuntimeRootTransform.gameObject.SetActive(true);

        if (SleepController != null)
        {
            SleepController.WakeUp();
        }
    }

    /// <summary>
    /// Prepares the pickup to be stored back inside the pool.
    /// </summary>
    public void PrepareForPoolStorage(Transform PoolRoot)
    {
        Transform RuntimeRootTransform = GetRuntimeRoot();

        EnsureCachedReferences();
        ResetPhysicsState();
        SetCollidersEnabled(false);
        Amount = 0f;
        RuntimeRootTransform.name = SourcePrefab != null ? SourcePrefab.name + "_Pooled" : "MoneyPickup_Pooled";
        RuntimeRootTransform.SetParent(PoolRoot, false);
        RuntimeRootTransform.gameObject.SetActive(false);
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
    public float GetAmount()
    {
        return CurrencyMath.RoundCurrency(Mathf.Max(0f, Amount));
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
    private void SetCollidersEnabled(bool IsEnabled)
    {
        if (CachedColliders == null)
        {
            return;
        }

        for (int Index = 0; Index < CachedColliders.Length; Index++)
        {
            if (CachedColliders[Index] == null)
            {
                continue;
            }

            CachedColliders[Index].enabled = IsEnabled;
        }
    }

    /// <summary>
    /// Caches missing rigidbody, collider and sleep controller references the first time they are needed.
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

        if (SleepController == null)
        {
            SleepController = GetComponent<MoneyPickupSleepController>();

            if (SleepController == null)
            {
                SleepController = GetComponentInChildren<MoneyPickupSleepController>(true);
            }
        }
    }
}