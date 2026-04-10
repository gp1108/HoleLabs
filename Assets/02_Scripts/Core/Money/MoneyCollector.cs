using UnityEngine;

/// <summary>
/// Helper component that resolves and collects looked money pickups.
/// This component no longer owns interact input directly.
/// Instead, a higher-level interaction controller decides when collection should happen.
/// </summary>
public sealed class MoneyCollector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera PlayerCamera;
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Header("Collection")]
    [SerializeField] private float CollectDistance = 4f;
    [SerializeField] private LayerMask CollectionLayers = ~0;
    [SerializeField] private QueryTriggerInteraction TriggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Debug")]
    [SerializeField] private bool DebugLogs = false;
    [SerializeField] private bool DrawDebugRay = false;

    private MoneyPickup CurrentLookedMoneyPickup;
    private bool IsExternalCollectionBlocked;

    public void SetExternalCollectionBlocked(bool IsBlocked)
    {
        IsExternalCollectionBlocked = IsBlocked;
    }

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

    private void Update()
    {
        if (IsExternalCollectionBlocked)
        {
            CurrentLookedMoneyPickup = null;
            return;
        }

        UpdateLookTarget();
    }

    public bool HasCurrentLookedMoneyPickup()
    {
        return CurrentLookedMoneyPickup != null;
    }

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

    private void CollectMoneyPickup(MoneyPickup MoneyPickup)
    {
        if (MoneyPickup == null)
        {
            return;
        }

        float Amount = Mathf.Max(0f, MoneyPickup.GetAmount());

        if (Amount <= 0f)
        {
            Log("Money pickup amount was zero. Returning pickup to pool.");
            MoneyPickup.ReturnToPool();
            return;
        }

        CurrencyWallet.AddCurrency(MoneyPickup.GetCurrencyType(), Amount);
        Log("Collected money pickup: " + MoneyPickup.name + " | Amount: " + Amount.ToString("0.00"));

        MoneyPickup.ReturnToPool();
    }

    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[MoneyCollector] " + Message, this);
    }
}