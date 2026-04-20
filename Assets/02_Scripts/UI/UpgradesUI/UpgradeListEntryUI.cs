using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manual list entry used to display one upgrade in a standard list layout.
/// The UpgradeDefinition is assigned directly in the inspector.
/// </summary>
public sealed class UpgradeListEntryUI : MonoBehaviour
{
    [Header("Data")]
    [Tooltip("Upgrade definition manually assigned to this list entry.")]
    [SerializeField] private UpgradeDefinition UpgradeDefinition;

    [Header("References")]
    [Tooltip("Icon image used to display the upgrade artwork.")]
    [SerializeField] private Image IconImage;

    [Tooltip("Text used to display the upgrade name.")]
    [SerializeField] private TMP_Text NameText;

    [Tooltip("Text used to display the upgrade description.")]
    [SerializeField] private TMP_Text DescriptionText;

    [Tooltip("Text used to display the current and maximum level.")]
    [SerializeField] private TMP_Text LevelText;

    [Tooltip("Text used to display the next level cost.")]
    [SerializeField] private TMP_Text CostText;

    [Tooltip("Text used to preview the next modifier or reward.")]
    [SerializeField] private TMP_Text EffectPreviewText;

    [Tooltip("Text used to display the current purchase state.")]
    [SerializeField] private TMP_Text StateText;

    [Tooltip("Button used to trigger the purchase attempt.")]
    [SerializeField] private Button PurchaseButton;

    [Header("Colors")]
    [Tooltip("Color used when the upgrade is currently purchasable.")]
    [SerializeField] private Color PurchasableColor = Color.white;

    [Tooltip("Color used when the upgrade is blocked.")]
    [SerializeField] private Color NotPurchasableColor = new Color(1f, 0.55f, 0.55f, 1f);

    [Tooltip("Color used when the upgrade is already maxed.")]
    [SerializeField] private Color MaxedColor = new Color(0.5f, 1f, 0.5f, 1f);

    private UpgradeManager UpgradeManager;

    /// <summary>
    /// Gets the manually assigned upgrade definition.
    /// </summary>
    public UpgradeDefinition GetUpgradeDefinition()
    {
        return UpgradeDefinition;
    }

    /// <summary>
    /// Initializes this manual list entry with runtime references.
    /// </summary>
    public void Initialize(UpgradeManager UpgradeManagerReference)
    {
        UpgradeManager = UpgradeManagerReference;

        if (PurchaseButton != null)
        {
            PurchaseButton.onClick.RemoveListener(HandlePurchaseButtonClicked);
            PurchaseButton.onClick.AddListener(HandlePurchaseButtonClicked);
        }

        RefreshView();
    }

    /// <summary>
    /// Refreshes all visual fields of this list entry.
    /// </summary>
    public void RefreshView()
    {
        if (UpgradeDefinition == null || UpgradeManager == null)
        {
            return;
        }

        int CurrentLevel = UpgradeManager.GetUpgradeLevel(UpgradeDefinition);
        int MaxLevel = UpgradeDefinition.GetMaxLevel();
        bool IsMaxed = CurrentLevel >= MaxLevel;
        UpgradePurchaseBlockReason BlockReason = UpgradeManager.GetPurchaseBlockReason(UpgradeDefinition);
        bool CanPurchase = BlockReason == UpgradePurchaseBlockReason.None;

        if (IconImage != null)
        {
            Sprite Icon = UpgradeDefinition.GetIcon();
            IconImage.sprite = Icon;
            IconImage.enabled = Icon != null;
        }

        if (NameText != null)
        {
            NameText.text = UpgradeDefinition.GetDisplayName();
        }

        if (DescriptionText != null)
        {
            DescriptionText.text = UpgradeDefinition.GetDescription();
        }

        if (LevelText != null)
        {
            LevelText.text = "Level " + CurrentLevel + " / " + MaxLevel;
        }

        if (CostText != null)
        {
            CostText.text = BuildCostText(CurrentLevel, IsMaxed);
        }

        if (EffectPreviewText != null)
        {
            EffectPreviewText.text = BuildEffectPreviewText(CurrentLevel, IsMaxed);
        }

        if (StateText != null)
        {
            StateText.text = BuildStateText(IsMaxed, BlockReason);
            StateText.color = GetStateColor(IsMaxed, CanPurchase);
        }

        if (PurchaseButton != null)
        {
            PurchaseButton.interactable = CanPurchase;
        }
    }

    /// <summary>
    /// Handles the purchase button click and forwards the request to the upgrade manager.
    /// </summary>
    private void HandlePurchaseButtonClicked()
    {
        if (UpgradeManager == null || UpgradeDefinition == null)
        {
            return;
        }

        UpgradeManager.TryPurchaseUpgrade(UpgradeDefinition);
    }

    /// <summary>
    /// Builds the cost label for the next level purchase.
    /// </summary>
    private string BuildCostText(int CurrentLevel, bool IsMaxed)
    {
        if (IsMaxed)
        {
            return "Cost: MAX";
        }

        int NextLevel = CurrentLevel + 1;
        UpgradeDefinition.UpgradeLevelCost NextCost = UpgradeDefinition.GetCostForLevel(NextLevel);

        if (NextCost == null)
        {
            return "Cost: N/A";
        }

        return "Cost: " + NextCost.GetCost().ToString("0.00") + " " + NextCost.GetCurrencyType();
    }

    /// <summary>
    /// Builds the preview text for the next relevant modifier or reward.
    /// </summary>
    private string BuildEffectPreviewText(int CurrentLevel, bool IsMaxed)
    {
        if (UpgradeDefinition == null)
        {
            return string.Empty;
        }

        var StatModifiers = UpgradeDefinition.GetStatModifiers();

        if (StatModifiers != null && StatModifiers.Count > 0)
        {
            UpgradeDefinition.StatModifierDefinition Modifier = StatModifiers[0];

            if (Modifier != null)
            {
                if (IsMaxed)
                {
                    float CurrentValue = Modifier.EvaluateValue(CurrentLevel);
                    return Modifier.GetStatType() + " " + Modifier.GetModifierType() + " " + CurrentValue.ToString("0.##");
                }

                float NextValue = Modifier.EvaluateValue(CurrentLevel + 1);
                return "Next: " + Modifier.GetStatType() + " " + Modifier.GetModifierType() + " " + NextValue.ToString("0.##");
            }
        }

        var Rewards = UpgradeDefinition.GetUnlockRewards();

        if (Rewards != null && Rewards.Count > 0)
        {
            UpgradeDefinition.UnlockRewardDefinition Reward = Rewards[0];

            if (Reward != null)
            {
                return "Unlock: " + Reward.GetRewardType();
            }
        }

        return "No preview";
    }

    /// <summary>
    /// Builds the current state label for the purchase area.
    /// </summary>
    private string BuildStateText(bool IsMaxed, UpgradePurchaseBlockReason BlockReason)
    {
        if (IsMaxed)
        {
            return "MAXED";
        }

        switch (BlockReason)
        {
            case UpgradePurchaseBlockReason.None:
                return "Available";

            case UpgradePurchaseBlockReason.MissingPrerequisite:
                return BuildMissingPrerequisiteText();

            case UpgradePurchaseBlockReason.NotEnoughCurrency:
                return "Not enough currency";

            case UpgradePurchaseBlockReason.MissingLevelCost:
                return "Missing cost config";

            case UpgradePurchaseBlockReason.MissingCurrencyWallet:
                return "Wallet missing";

            case UpgradePurchaseBlockReason.MissingDefinition:
                return "Invalid upgrade";

            case UpgradePurchaseBlockReason.AlreadyMaxLevel:
                return "MAXED";

            default:
                return "Unavailable";
        }
    }

    /// <summary>
    /// Builds a compact state text describing the first unmet prerequisite.
    /// </summary>
    private string BuildMissingPrerequisiteText()
    {
        if (UpgradeManager == null || UpgradeDefinition == null)
        {
            return "Prerequisite missing";
        }

        if (!UpgradeManager.TryGetFirstUnmetPrerequisite(
                UpgradeDefinition,
                out UpgradeDefinition.UpgradePrerequisiteDefinition UnmetPrerequisite
            ) || UnmetPrerequisite == null)
        {
            return "Prerequisite missing";
        }

        UpgradeDefinition RequiredDefinition = UnmetPrerequisite.GetRequiredUpgradeDefinition();
        string RequiredName = RequiredDefinition != null ? RequiredDefinition.GetDisplayName() : "Missing Upgrade Reference";

        return "Requires " + RequiredName + " Lv " + UnmetPrerequisite.GetRequiredLevel();
    }

    /// <summary>
    /// Gets the color that matches the current purchase state.
    /// </summary>
    private Color GetStateColor(bool IsMaxed, bool CanPurchase)
    {
        if (IsMaxed)
        {
            return MaxedColor;
        }

        return CanPurchase ? PurchasableColor : NotPurchasableColor;
    }
}