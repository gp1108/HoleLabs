using UnityEngine;

/// <summary>
/// Helper component that resolves and collects looked money pickups.
/// This component no longer owns interact input directly.
/// Instead, a higher-level interaction controller decides when collection should happen.
/// </summary>
public sealed class MoneyCollector : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera used to cast the collection ray.")]
    [SerializeField] private Camera PlayerCamera;

    [Tooltip("Wallet that receives the collected money.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Header("Collection")]
    [Tooltip("Maximum distance used to detect money pickups.")]
    [SerializeField] private float CollectDistance = 4f;

    [Tooltip("Layers considered valid for money collection.")]
    [SerializeField] private LayerMask CollectionLayers = ~0;

    [Tooltip("Defines whether trigger colliders are considered by the collection ray.")]
    [SerializeField] private QueryTriggerInteraction TriggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Debug")]
    [Tooltip("Logs money collection operations to the console.")]
    [SerializeField] private bool DebugLogs = false;

    [Tooltip("Draws the collection ray in the Scene view.")]
    [SerializeField] private bool DrawDebugRay = false;

    /// <summary>
    /// Money pickup currently under the center-screen ray.
    /// </summary>
    private MoneyPickup CurrentLookedMoneyPickup;

    /// <summary>
    /// Whether money collection input is currently blocked by an external modal state.
    /// </summary>
    private bool IsExternalCollectionBlocked;

    /// <summary>
    /// Allows external systems to block or restore money collection processing.
    /// </summary>
    /// <param name="IsBlocked">True to block collection, false to restore it.</param>
    public void SetExternalCollectionBlocked(bool IsBlocked)
    {
        IsExternalCollectionBlocked = IsBlocked;
    }

    /// <summary>
    /// Caches required references.
    /// </summary>
    private void Awake()
    {
        if (PlayerCamera == null)
        {
            PlayerController PlayerController = GetComponent<PlayerController>();

            if (PlayerController != null && PlayerController.PlayerCamera != null)
            {
                PlayerCamera = PlayerController.PlayerCamera;
            }
            else
            {
                PlayerCamera = Camera.main;
            }
        }

        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        if (PlayerCamera == null)
        {
            Debug.LogError("[MoneyCollector] PlayerCamera is missing.", this);
            enabled = false;
            return;
        }

        if (CurrencyWallet == null)
        {
            Debug.LogError("[MoneyCollector] CurrencyWallet is missing.", this);
            enabled = false;
            return;
        }
    }

    /// <summary>
    /// Updates the current looked money pickup.
    /// </summary>
    private void Update()
    {
        if (IsExternalCollectionBlocked)
        {
            CurrentLookedMoneyPickup = null;
            return;
        }

        UpdateLookTarget();
    }

    /// <summary>
    /// Returns whether the player is currently looking at a valid money pickup.
    /// </summary>
    public bool HasCurrentLookedMoneyPickup()
    {
        return CurrentLookedMoneyPickup != null;
    }

    /// <summary>
    /// Attempts to collect the money pickup currently looked at by the player.
    /// </summary>
    /// <returns>True when a pickup was successfully collected.</returns>
    public bool TryCollectCurrentLookedMoney()
    {
        if (IsExternalCollectionBlocked)
        {
            return false;
        }

        if (CurrentLookedMoneyPickup == null)
        {
            return false;
        }

        CollectMoneyPickup(CurrentLookedMoneyPickup);
        CurrentLookedMoneyPickup = null;
        return true;
    }

    /// <summary>
    /// Updates the money pickup currently looked at by the player.
    /// </summary>
    private void UpdateLookTarget()
    {
        CurrentLookedMoneyPickup = null;

        if (PlayerCamera == null)
        {
            return;
        }

        Ray ViewRay = PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (DrawDebugRay)
        {
            Debug.DrawRay(ViewRay.origin, ViewRay.direction * CollectDistance, Color.green);
        }

        if (!Physics.Raycast(ViewRay, out RaycastHit HitInfo, CollectDistance, CollectionLayers, TriggerInteraction))
        {
            return;
        }

        CurrentLookedMoneyPickup = ResolveMoneyPickup(HitInfo);
    }

    /// <summary>
    /// Resolves a money pickup from the current raycast hit.
    /// </summary>
    private MoneyPickup ResolveMoneyPickup(RaycastHit HitInfo)
    {
        if (HitInfo.collider == null)
        {
            return null;
        }

        MoneyPickup MoneyPickup = HitInfo.collider.GetComponent<MoneyPickup>() ?? HitInfo.collider.GetComponentInParent<MoneyPickup>();

        if (MoneyPickup != null)
        {
            return MoneyPickup;
        }

        if (HitInfo.rigidbody != null)
        {
            MoneyPickup = HitInfo.rigidbody.GetComponent<MoneyPickup>() ?? HitInfo.rigidbody.GetComponentInParent<MoneyPickup>();
        }

        return MoneyPickup;
    }

    /// <summary>
    /// Transfers the pickup value to the wallet and returns the pickup to the pool.
    /// </summary>
    private void CollectMoneyPickup(MoneyPickup MoneyPickup)
    {
        if (MoneyPickup == null)
        {
            return;
        }

        int Amount = Mathf.Max(0, MoneyPickup.GetAmount());

        if (Amount <= 0)
        {
            Log("Money pickup amount was zero. Returning pickup to pool.");
            MoneyPickup.ReturnToPool();
            return;
        }

        CurrencyWallet.AddCurrency(MoneyPickup.GetCurrencyType(), Amount);
        Log("Collected money pickup: " + MoneyPickup.name + " | Amount: " + Amount);

        MoneyPickup.ReturnToPool();
    }

    /// <summary>
    /// Writes a collector-specific debug message when logging is enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[MoneyCollector] " + Message, this);
    }
}