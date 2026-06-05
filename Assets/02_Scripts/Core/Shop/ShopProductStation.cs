using UnityEngine;

/// <summary>
/// World station that sells shop products.
/// Products are separated from applied upgrades so the shop can deliver physical items that the player must place later.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class ShopProductStation : MonoBehaviour
{
    /// <summary>
    /// Describes why a product cannot currently be purchased.
    /// </summary>
    public enum ProductPurchaseBlockReason
    {
        None = 0,
        MissingProduct = 1,
        MissingWallet = 2,
        MissingUpgradeManager = 3,
        MissingRequiredFeatureFlag = 4,
        MissingDeliveredItemUnlock = 5,
        NotEnoughCurrency = 6,
        MissingDeliveredItemDefinition = 7,
        MissingWorldPrefab = 8,
        MissingAppliedUpgradeDefinition = 9,
        MissingDeliveryPoint = 10
    }

    [Header("References")]
    [Tooltip("Panel controlled by this station.")]
    [SerializeField] private ShopProductPanelUI ProductPanelUI;

    [Tooltip("Wallet used to spend product costs.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Tooltip("Upgrade manager used for availability checks and immediate upgrade application.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional prompt root enabled only while the player is inside the station range.")]
    [SerializeField] private GameObject PromptRoot;

    [Header("Delivery")]
    [Tooltip("World point where purchased physical products are spawned.")]
    [SerializeField] private Transform DeliveryPoint;

    [Tooltip("Impulse applied to delivered physical products.")]
    [SerializeField] private float DeliveryImpulse = 1.5f;

    [Tooltip("If true, the delivered item is rotated using the delivery point rotation.")]
    [SerializeField] private bool UseDeliveryPointRotation = true;

    [Header("Debug")]
    [Tooltip("Logs shop product purchase flow.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Player currently inside the station range.
    /// </summary>
    private UpgradeShopInteractor CurrentInteractor;

    /// <summary>
    /// Resolves missing runtime references.
    /// </summary>
    private void Awake()
    {
        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        if (UpgradeManager == null)
        {
            UpgradeManager = FindFirstObjectByType<UpgradeManager>();
        }

        if (ProductPanelUI != null)
        {
            ProductPanelUI.Initialize(this);
        }
    }

    /// <summary>
    /// Gets the product panel owned by this station.
    /// </summary>
    public ShopProductPanelUI GetProductPanelUI()
    {
        return ProductPanelUI;
    }

    /// <summary>
    /// Returns whether the provided interactor is currently the registered nearby player.
    /// </summary>
    public bool IsInteractorRegistered(UpgradeShopInteractor Interactor)
    {
        return CurrentInteractor == Interactor;
    }

    /// <summary>
    /// Returns why the provided product cannot currently be purchased.
    /// </summary>
    /// <param name="ProductDefinition">Product to validate.</param>
    /// <returns>Current purchase block reason.</returns>
    public ProductPurchaseBlockReason GetPurchaseBlockReason(ShopProductDefinition ProductDefinition)
    {
        if (ProductDefinition == null)
        {
            return ProductPurchaseBlockReason.MissingProduct;
        }

        if (CurrencyWallet == null)
        {
            return ProductPurchaseBlockReason.MissingWallet;
        }

        if (ProductDefinition.GetRequiresFeatureFlag() ||
            ProductDefinition.GetRequiresDeliveredItemUnlock() ||
            ProductDefinition.ShouldApplyUpgradeImmediately())
        {
            if (UpgradeManager == null)
            {
                return ProductPurchaseBlockReason.MissingUpgradeManager;
            }
        }

        if (ProductDefinition.GetRequiresFeatureFlag() &&
            !UpgradeManager.IsFeatureUnlocked(ProductDefinition.GetRequiredFeatureFlagId()))
        {
            return ProductPurchaseBlockReason.MissingRequiredFeatureFlag;
        }

        if (ProductDefinition.GetRequiresDeliveredItemUnlock() &&
            !UpgradeManager.IsItemUnlocked(ProductDefinition.GetDeliveredItemDefinition()))
        {
            return ProductPurchaseBlockReason.MissingDeliveredItemUnlock;
        }

        if (!CurrencyWallet.HasEnough(ProductDefinition.GetCurrencyType(), ProductDefinition.GetCost()))
        {
            return ProductPurchaseBlockReason.NotEnoughCurrency;
        }

        if (ProductDefinition.ShouldSpawnWorldItem())
        {
            ItemDefinition DeliveredItemDefinition = ProductDefinition.GetDeliveredItemDefinition();

            if (DeliveredItemDefinition == null)
            {
                return ProductPurchaseBlockReason.MissingDeliveredItemDefinition;
            }

            if (DeliveredItemDefinition.GetWorldPrefab() == null)
            {
                return ProductPurchaseBlockReason.MissingWorldPrefab;
            }

            if (DeliveryPoint == null)
            {
                return ProductPurchaseBlockReason.MissingDeliveryPoint;
            }
        }

        if (ProductDefinition.ShouldApplyUpgradeImmediately() &&
            ProductDefinition.GetAppliedUpgradeDefinition() == null)
        {
            return ProductPurchaseBlockReason.MissingAppliedUpgradeDefinition;
        }

        return ProductPurchaseBlockReason.None;
    }

    /// <summary>
    /// Attempts to purchase the provided product and deliver its configured result.
    /// </summary>
    /// <param name="ProductDefinition">Product to purchase.</param>
    /// <returns>True when the purchase completed successfully.</returns>
    public bool TryPurchaseProduct(ShopProductDefinition ProductDefinition)
    {
        ProductPurchaseBlockReason BlockReason = GetPurchaseBlockReason(ProductDefinition);

        if (BlockReason != ProductPurchaseBlockReason.None)
        {
            Log("Product purchase blocked: " + BlockReason);
            return false;
        }

        if (!CurrencyWallet.TrySpendCurrency(ProductDefinition.GetCurrencyType(), ProductDefinition.GetCost()))
        {
            Log("Product purchase failed while spending currency: " + ProductDefinition.GetDisplayName());
            return false;
        }

        if (ProductDefinition.ShouldSpawnWorldItem() && !SpawnDeliveredWorldItem(ProductDefinition))
        {
            Log("Product delivery failed after payment: " + ProductDefinition.GetDisplayName());
            return false;
        }

        if (ProductDefinition.ShouldApplyUpgradeImmediately())
        {
            ApplyProductUpgrade(ProductDefinition);
        }

        if (ProductPanelUI != null)
        {
            ProductPanelUI.RefreshAll();
        }

        Log("Purchased shop product: " + ProductDefinition.GetDisplayName());
        return true;
    }

    /// <summary>
    /// Spawns the physical item delivered by the provided product.
    /// </summary>
    /// <param name="ProductDefinition">Product that defines the delivered item.</param>
    /// <returns>True when the world item was spawned successfully.</returns>
    private bool SpawnDeliveredWorldItem(ShopProductDefinition ProductDefinition)
    {
        ItemDefinition DeliveredItemDefinition = ProductDefinition.GetDeliveredItemDefinition();
        GameObject WorldPrefab = DeliveredItemDefinition.GetWorldPrefab();
        Quaternion Rotation = UseDeliveryPointRotation ? DeliveryPoint.rotation : Quaternion.identity;

        GameObject WorldObject = Instantiate(WorldPrefab, DeliveryPoint.position, Rotation);
        WorldItem WorldItem = WorldObject.GetComponent<WorldItem>() ?? WorldObject.GetComponentInChildren<WorldItem>(true);

        if (WorldItem != null)
        {
            ItemInstance ItemInstance = DeliveredItemDefinition.CreateRuntimeInstance(ProductDefinition.GetDeliveredAmount());
            WorldItem.ApplyItemInstance(ItemInstance);

            Rigidbody RigidbodyComponent = WorldItem.GetRigidbody();
            if (RigidbodyComponent != null && DeliveryImpulse > 0f)
            {
                RigidbodyComponent.AddForce(DeliveryPoint.forward * DeliveryImpulse, ForceMode.Impulse);
            }
        }

        return true;
    }

    /// <summary>
    /// Applies the upgrade effect configured by the provided product.
    /// </summary>
    /// <param name="ProductDefinition">Product that defines the upgrade effect.</param>
    private void ApplyProductUpgrade(ShopProductDefinition ProductDefinition)
    {
        UpgradeDefinition UpgradeDefinition = ProductDefinition.GetAppliedUpgradeDefinition();
        int CurrentLevel = UpgradeManager.GetUpgradeLevel(UpgradeDefinition);
        int TargetLevel = CurrentLevel;

        switch (ProductDefinition.GetApplyMode())
        {
            case ShopProductDefinition.UpgradeApplyMode.SetToLevel:
                TargetLevel = ProductDefinition.GetTargetUpgradeLevel();
                break;

            case ShopProductDefinition.UpgradeApplyMode.AddLevels:
                TargetLevel = CurrentLevel + ProductDefinition.GetUpgradeLevelIncrement();
                break;
        }

        UpgradeManager.SetUpgradeLevel(UpgradeDefinition, TargetLevel);
    }

    /// <summary>
    /// Registers the player interactor entering the station range.
    /// </summary>
    private void OnTriggerEnter(Collider Other)
    {
        UpgradeShopInteractor Interactor = Other.GetComponentInParent<UpgradeShopInteractor>();

        if (Interactor == null)
        {
            return;
        }

        CurrentInteractor = Interactor;
        CurrentInteractor.SetNearbyProductStation(this);

        if (PromptRoot != null)
        {
            PromptRoot.SetActive(true);
        }
    }

    /// <summary>
    /// Unregisters the player interactor leaving the station range.
    /// </summary>
    private void OnTriggerExit(Collider Other)
    {
        UpgradeShopInteractor Interactor = Other.GetComponentInParent<UpgradeShopInteractor>();

        if (Interactor == null || CurrentInteractor != Interactor)
        {
            return;
        }

        CurrentInteractor.ClearNearbyProductStation(this);

        if (PromptRoot != null)
        {
            PromptRoot.SetActive(false);
        }

        CurrentInteractor = null;
    }

    /// <summary>
    /// Logs product station messages when debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[ShopProductStation] " + Message, this);
    }
}
