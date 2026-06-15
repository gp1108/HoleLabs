using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manual UI entry used to display and purchase one shop product.
/// The product definition is assigned directly in the inspector.
/// </summary>
public sealed class ShopProductListEntryUI : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("Shop product manually assigned to this entry.")]
    [SerializeField] private ShopProductDefinition ProductDefinition;

    [Header("References")]
    [Tooltip("Icon image used to display the product artwork.")]
    [SerializeField] private Image IconImage;

    [Tooltip("Text used to display the product name.")]
    [SerializeField] private TMP_Text NameText;

    [Tooltip("Text used to display the product description.")]
    [SerializeField] private TMP_Text DescriptionText;

    [Tooltip("Text used to display the product cost.")]
    [SerializeField] private TMP_Text CostText;

    [Tooltip("Text used to display current purchase state.")]
    [SerializeField] private TMP_Text StateText;

    [Tooltip("Optional text placed on the purchase button. When empty, only State Text is updated.")]
    [SerializeField] private TMP_Text PurchaseButtonLabelText;

    [Tooltip("Button used to trigger the purchase attempt.")]
    [SerializeField] private Button PurchaseButton;

    [Header("Colors")]
    [Tooltip("Color used when the product is purchasable.")]
    [SerializeField] private Color PurchasableColor = Color.white;

    [Tooltip("Color used when the product is blocked.")]
    [SerializeField] private Color NotPurchasableColor = new Color(1f, 0.55f, 0.55f, 1f);

    /// <summary>
    /// Product station used to execute purchases.
    /// </summary>
    private ShopProductStation OwnerStation;

    /// <summary>
    /// Initializes this entry with the owning product station.
    /// </summary>
    /// <param name="Station">Product station that owns this entry.</param>
    public void Initialize(ShopProductStation Station)
    {
        OwnerStation = Station;

        if (PurchaseButton != null)
        {
            PurchaseButton.onClick.RemoveListener(HandlePurchaseButtonClicked);
            PurchaseButton.onClick.AddListener(HandlePurchaseButtonClicked);
        }

        RefreshView();
    }

    /// <summary>
    /// Refreshes all visual fields of this product entry.
    /// </summary>
    public void RefreshView()
    {
        if (ProductDefinition == null)
        {
            return;
        }

        if (OwnerStation != null && OwnerStation.ShouldHideProductEntry(ProductDefinition))
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            return;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        ShopProductStation.ProductPurchaseBlockReason BlockReason = OwnerStation != null
            ? OwnerStation.GetPurchaseBlockReason(ProductDefinition)
            : ShopProductStation.ProductPurchaseBlockReason.MissingProduct;

        ShopProductStation.ProductPurchaseAction PurchaseAction = OwnerStation != null
            ? OwnerStation.GetPurchaseAction(ProductDefinition)
            : ShopProductStation.ProductPurchaseAction.None;

        bool CanPurchase = BlockReason == ShopProductStation.ProductPurchaseBlockReason.None;

        if (IconImage != null)
        {
            Sprite Icon = ProductDefinition.GetIcon();
            IconImage.sprite = Icon;
            IconImage.enabled = Icon != null;
        }

        if (NameText != null)
        {
            NameText.text = ProductDefinition.GetDisplayName();
        }

        if (DescriptionText != null)
        {
            DescriptionText.text = ProductDefinition.GetDescription();
        }

        if (CostText != null)
        {
            CostText.text = BuildCostText(PurchaseAction);
        }

        if (StateText != null)
        {
            StateText.text = BuildStateText(BlockReason, PurchaseAction);
            StateText.color = CanPurchase ? PurchasableColor : NotPurchasableColor;
        }

        if (PurchaseButtonLabelText != null)
        {
            PurchaseButtonLabelText.text = BuildButtonText(BlockReason, PurchaseAction);
        }

        if (PurchaseButton != null)
        {
            PurchaseButton.interactable = CanPurchase;
        }
    }

    /// <summary>
    /// Handles the purchase button click.
    /// </summary>
    private void HandlePurchaseButtonClicked()
    {
        if (OwnerStation == null || ProductDefinition == null)
        {
            return;
        }

        OwnerStation.TryPurchaseProduct(ProductDefinition);
    }

    /// <summary>
    /// Builds the cost text using the current action cost resolved by the station.
    /// </summary>
    /// <param name="PurchaseAction">Current purchase action.</param>
    /// <returns>Formatted cost label.</returns>
    private string BuildCostText(ShopProductStation.ProductPurchaseAction PurchaseAction)
    {
        float EffectiveCost = OwnerStation != null
            ? OwnerStation.GetEffectivePurchaseCost(ProductDefinition)
            : ProductDefinition.GetCost();

        string CostPrefix = PurchaseAction == ShopProductStation.ProductPurchaseAction.Reissue
            ? "Reissue Cost: "
            : "Cost: ";

        return CostPrefix + EffectiveCost.ToString("0.00") + " " + ProductDefinition.GetCurrencyType();
    }

    /// <summary>
    /// Builds a compact state label for the current purchase block reason.
    /// </summary>
    /// <param name="BlockReason">Current purchase block reason.</param>
    /// <param name="PurchaseAction">Current purchase action.</param>
    /// <returns>State label.</returns>
    private string BuildStateText(ShopProductStation.ProductPurchaseBlockReason BlockReason, ShopProductStation.ProductPurchaseAction PurchaseAction)
    {
        switch (BlockReason)
        {
            case ShopProductStation.ProductPurchaseBlockReason.None:
                return PurchaseAction == ShopProductStation.ProductPurchaseAction.Reissue ? "Reissue available" : "Available";
            case ShopProductStation.ProductPurchaseBlockReason.NotEnoughCurrency:
                return "Not enough credits";
            case ShopProductStation.ProductPurchaseBlockReason.MissingRequiredFeatureFlag:
                return "Locked";
            case ShopProductStation.ProductPurchaseBlockReason.MissingDeliveredItemUnlock:
                return "Item not unlocked";
            case ShopProductStation.ProductPurchaseBlockReason.MissingWorldPrefab:
                return "Missing world prefab";
            case ShopProductStation.ProductPurchaseBlockReason.MissingDeliveryPoint:
                return "Missing delivery point";
            case ShopProductStation.ProductPurchaseBlockReason.UniqueProductInstalled:
                return "Installed";
            default:
                return BlockReason.ToString();
        }
    }

    /// <summary>
    /// Builds the optional purchase button label.
    /// </summary>
    /// <param name="BlockReason">Current purchase block reason.</param>
    /// <param name="PurchaseAction">Current purchase action.</param>
    /// <returns>Button label.</returns>
    private string BuildButtonText(ShopProductStation.ProductPurchaseBlockReason BlockReason, ShopProductStation.ProductPurchaseAction PurchaseAction)
    {
        if (BlockReason == ShopProductStation.ProductPurchaseBlockReason.UniqueProductInstalled)
        {
            return "Installed";
        }

        if (BlockReason != ShopProductStation.ProductPurchaseBlockReason.None)
        {
            return "Locked";
        }

        return PurchaseAction == ShopProductStation.ProductPurchaseAction.Reissue ? "Reissue" : "Buy";
    }
}
