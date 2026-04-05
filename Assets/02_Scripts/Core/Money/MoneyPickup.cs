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

    public void BindPool(MoneyPickupPool OwnerPoolValue, GameObject SourcePrefabValue)
    {
        OwnerPool = OwnerPoolValue;
        SourcePrefab = SourcePrefabValue;
    }

    public void PrepareForReuse(Vector3 Position, Quaternion Rotation)
    {
        Transform RuntimeRootTransform = GetRuntimeRoot();
        RuntimeRootTransform.SetParent(null, true);
        RuntimeRootTransform.SetPositionAndRotation(Position, Rotation);

        EnsureCachedReferences();
        ResetPhysicsState();
        SetCollidersEnabled(true);
        RuntimeRootTransform.gameObject.SetActive(true);
    }

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

    public bool ReturnToPool()
    {
        if (OwnerPool == null || SourcePrefab == null)
        {
            return false;
        }

        OwnerPool.ReturnPickup(this, SourcePrefab);
        return true;
    }

    public CurrencyWallet.CurrencyType GetCurrencyType()
    {
        return CurrencyType;
    }

    public float GetAmount()
    {
        return CurrencyMath.RoundCurrency(Mathf.Max(0f, Amount));
    }

    public Rigidbody GetCachedRigidbody()
    {
        EnsureCachedReferences();
        return CachedRigidbody;
    }

    public Transform GetRuntimeRoot()
    {
        if (RuntimeRoot == null)
        {
            RuntimeRoot = transform;
        }

        return RuntimeRoot;
    }

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