using System;
using UnityEngine;

/// <summary>
/// Defines one purchasable shop product.
/// Products can deliver physical items, apply upgrades immediately, or use limited stock rules.
/// Unique reissue products allow exactly one loose physical instance until the item is installed.
/// </summary>
[CreateAssetMenu(fileName = "ShopProduct_", menuName = "Game/Shop/Shop Product Definition")]
public sealed class ShopProductDefinition : ScriptableObject
{
    /// <summary>
    /// Defines what happens after the product cost is paid.
    /// </summary>
    public enum ProductDeliveryMode
    {
        SpawnWorldItem = 0,
        ApplyUpgradeImmediately = 1,
        SpawnWorldItemAndApplyUpgradeImmediately = 2
    }

    /// <summary>
    /// Defines how the referenced upgrade level is modified when this product applies an upgrade.
    /// </summary>
    public enum UpgradeApplyMode
    {
        SetToLevel = 0,
        AddLevels = 1
    }

    /// <summary>
    /// Defines how this product is limited by existing owned runtime instances.
    /// </summary>
    public enum ProductStockMode
    {
        Unlimited = 0,
        UniqueReissueUntilInstalled = 1
    }

    /// <summary>
    /// Defines how much the player pays when reissuing an already loose unique product.
    /// </summary>
    public enum UniqueReissueCostMode
    {
        Free = 0,
        FullCost = 1,
        CustomCost = 2
    }

    /// <summary>
    /// Defines how this product entry behaves after the unique product has been installed.
    /// </summary>
    public enum InstalledEntryMode
    {
        ShowAsInstalled = 0,
        HideEntry = 1
    }

    [Header("Identity")]
    [Tooltip("Unique runtime identifier used to distinguish this shop product from others.")]
    [SerializeField] private string ProductId;

    [Tooltip("Display name shown in shop UI.")]
    [SerializeField] private string DisplayName;

    [Tooltip("Description shown in shop UI.")]
    [TextArea]
    [SerializeField] private string Description;

    [Tooltip("Optional icon shown in shop UI.")]
    [SerializeField] private Sprite Icon;

    [Header("Cost")]
    [Tooltip("Currency spent when this product is purchased. Use Credits for current gameplay.")]
    [SerializeField] private CurrencyWallet.CurrencyType CurrencyType = CurrencyWallet.CurrencyType.Credits;

    [Tooltip("Amount spent when this product is purchased.")]
    [SerializeField] private float Cost = 10f;

    [Header("Availability")]
    [Tooltip("If true, this product only becomes purchasable after a feature flag is unlocked.")]
    [SerializeField] private bool RequiresFeatureFlag = false;

    [Tooltip("Feature flag required when Requires Feature Flag is enabled.")]
    [SerializeField] private string RequiredFeatureFlagId;

    [Tooltip("If true, this product only becomes purchasable after the delivered item has been unlocked by UpgradeManager.")]
    [SerializeField] private bool RequiresDeliveredItemUnlock = false;

    [Header("Delivery")]
    [Tooltip("How this product delivers its reward after payment.")]
    [SerializeField] private ProductDeliveryMode DeliveryMode = ProductDeliveryMode.SpawnWorldItem;

    [Tooltip("Item physically spawned when the delivery mode includes world item delivery.")]
    [SerializeField] private ItemDefinition DeliveredItemDefinition;

    [Tooltip("Amount stored in the delivered item instance.")]
    [SerializeField] private int DeliveredAmount = 1;

    [Header("Stock")]
    [Tooltip("Stock policy used by this product. Use Unique Reissue Until Installed for physical one-off machines such as the researcher.")]
    [SerializeField] private ProductStockMode StockMode = ProductStockMode.Unlimited;

    [Tooltip("Stable logical id used for debugging unique products. If empty, Product Id is used as fallback.")]
    [SerializeField] private string UniqueGroupId;

    [Tooltip("Cost policy used when the unique product already exists loose and the player reissues it.")]
    [SerializeField] private UniqueReissueCostMode ReissueCostMode = UniqueReissueCostMode.FullCost;

    [Tooltip("Custom credit cost used only when Reissue Cost Mode is Custom Cost.")]
    [SerializeField] private float CustomReissueCost = 0f;

    [Tooltip("How this shop entry behaves after the unique product has been installed.")]
    [SerializeField] private InstalledEntryMode InstalledProductEntryMode = InstalledEntryMode.ShowAsInstalled;

    [Header("Immediate Upgrade")]
    [Tooltip("Upgrade modified when the delivery mode includes immediate upgrade application.")]
    [SerializeField] private UpgradeDefinition AppliedUpgradeDefinition;

    [Tooltip("How the referenced upgrade level is modified.")]
    [SerializeField] private UpgradeApplyMode ApplyMode = UpgradeApplyMode.SetToLevel;

    [Tooltip("Target level used by Set To Level mode.")]
    [SerializeField] private int TargetUpgradeLevel = 1;

    [Tooltip("Level increment used by Add Levels mode.")]
    [SerializeField] private int UpgradeLevelIncrement = 1;

    /// <summary>
    /// Gets the unique product identifier.
    /// </summary>
    public string GetProductId()
    {
        return ProductId;
    }

    /// <summary>
    /// Gets the display name shown in shop UI.
    /// </summary>
    public string GetDisplayName()
    {
        return DisplayName;
    }

    /// <summary>
    /// Gets the description shown in shop UI.
    /// </summary>
    public string GetDescription()
    {
        return Description;
    }

    /// <summary>
    /// Gets the icon shown in shop UI.
    /// </summary>
    public Sprite GetIcon()
    {
        return Icon;
    }

    /// <summary>
    /// Gets the currency spent by this product.
    /// </summary>
    public CurrencyWallet.CurrencyType GetCurrencyType()
    {
        return CurrencyWallet.NormalizeCurrencyType(CurrencyType);
    }

    /// <summary>
    /// Gets the rounded purchase cost.
    /// </summary>
    public float GetCost()
    {
        return CurrencyMath.RoundCurrency(Mathf.Max(0f, Cost));
    }

    /// <summary>
    /// Gets whether this product requires a feature flag before purchase.
    /// </summary>
    public bool GetRequiresFeatureFlag()
    {
        return RequiresFeatureFlag;
    }

    /// <summary>
    /// Gets the required feature flag id.
    /// </summary>
    public string GetRequiredFeatureFlagId()
    {
        return RequiredFeatureFlagId;
    }

    /// <summary>
    /// Gets whether the delivered item must be unlocked before purchase.
    /// </summary>
    public bool GetRequiresDeliveredItemUnlock()
    {
        return RequiresDeliveredItemUnlock;
    }

    /// <summary>
    /// Gets the configured delivery mode.
    /// </summary>
    public ProductDeliveryMode GetDeliveryMode()
    {
        return DeliveryMode;
    }

    /// <summary>
    /// Gets whether this product should spawn a physical world item.
    /// </summary>
    public bool ShouldSpawnWorldItem()
    {
        return DeliveryMode == ProductDeliveryMode.SpawnWorldItem ||
               DeliveryMode == ProductDeliveryMode.SpawnWorldItemAndApplyUpgradeImmediately;
    }

    /// <summary>
    /// Gets whether this product should apply an upgrade immediately.
    /// </summary>
    public bool ShouldApplyUpgradeImmediately()
    {
        return DeliveryMode == ProductDeliveryMode.ApplyUpgradeImmediately ||
               DeliveryMode == ProductDeliveryMode.SpawnWorldItemAndApplyUpgradeImmediately;
    }

    /// <summary>
    /// Gets the delivered item definition.
    /// </summary>
    public ItemDefinition GetDeliveredItemDefinition()
    {
        return DeliveredItemDefinition;
    }

    /// <summary>
    /// Gets the delivered item amount.
    /// </summary>
    public int GetDeliveredAmount()
    {
        return Mathf.Max(1, DeliveredAmount);
    }

    /// <summary>
    /// Gets the stock policy configured for this product.
    /// </summary>
    public ProductStockMode GetStockMode()
    {
        return StockMode;
    }

    /// <summary>
    /// Gets whether this product uses unique reissue stock rules.
    /// </summary>
    public bool UsesUniqueReissueStock()
    {
        return StockMode == ProductStockMode.UniqueReissueUntilInstalled;
    }

    /// <summary>
    /// Gets the stable unique group id used for debugging and future save-safe ownership policies.
    /// </summary>
    public string GetUniqueGroupId()
    {
        if (!string.IsNullOrWhiteSpace(UniqueGroupId))
        {
            return UniqueGroupId;
        }

        if (!string.IsNullOrWhiteSpace(ProductId))
        {
            return ProductId;
        }

        return DeliveredItemDefinition != null ? DeliveredItemDefinition.GetItemId() : string.Empty;
    }

    /// <summary>
    /// Gets the cost policy used when reissuing a loose unique product.
    /// </summary>
    public UniqueReissueCostMode GetReissueCostMode()
    {
        return ReissueCostMode;
    }

    /// <summary>
    /// Gets the rounded custom reissue cost.
    /// </summary>
    public float GetCustomReissueCost()
    {
        return CurrencyMath.RoundCurrency(Mathf.Max(0f, CustomReissueCost));
    }

    /// <summary>
    /// Gets the installed entry behaviour used by unique products.
    /// </summary>
    public InstalledEntryMode GetInstalledProductEntryMode()
    {
        return InstalledProductEntryMode;
    }

    /// <summary>
    /// Gets the effective cost used when the product is reissued instead of bought for the first time.
    /// </summary>
    public float GetReissueCost()
    {
        switch (ReissueCostMode)
        {
            case UniqueReissueCostMode.Free:
                return 0f;

            case UniqueReissueCostMode.CustomCost:
                return GetCustomReissueCost();

            case UniqueReissueCostMode.FullCost:
            default:
                return GetCost();
        }
    }

    /// <summary>
    /// Gets the upgrade affected by this product when immediate application is enabled.
    /// </summary>
    public UpgradeDefinition GetAppliedUpgradeDefinition()
    {
        return AppliedUpgradeDefinition;
    }

    /// <summary>
    /// Gets the configured upgrade apply mode.
    /// </summary>
    public UpgradeApplyMode GetApplyMode()
    {
        return ApplyMode;
    }

    /// <summary>
    /// Gets the target level used by Set To Level mode.
    /// </summary>
    public int GetTargetUpgradeLevel()
    {
        return Mathf.Max(0, TargetUpgradeLevel);
    }

    /// <summary>
    /// Gets the level increment used by Add Levels mode.
    /// </summary>
    public int GetUpgradeLevelIncrement()
    {
        return Mathf.Max(1, UpgradeLevelIncrement);
    }
}
