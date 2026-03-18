using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Represents one research upgrade entry in the UI.
/// This component is responsible only for visual data binding and purchase interaction.
/// </summary>
public sealed class UpgradeEntryUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Image used to display the upgrade icon.")]
    [SerializeField] private Image IconImage;

    [Tooltip("Text used to display the upgrade name.")]
    [SerializeField] private TMP_Text NameText;

    [Tooltip("Text used to display the upgrade description.")]
    [SerializeField] private TMP_Text DescriptionText;

    [Tooltip("Text used to display the current level and max level.")]
    [SerializeField] private TMP_Text LevelText;

    [Tooltip("Text used to display the next purchase cost.")]
    [SerializeField] private TMP_Text CostText;

    [Tooltip("Text used to display the preview of the next effect or current state.")]
    [SerializeField] private TMP_Text EffectPreviewText;

    [Tooltip("Text used to display the current availability state.")]
    [SerializeField] private TMP_Text StateText;

    [Tooltip("Button used to purchase the next upgrade level.")]
    [SerializeField] private Button PurchaseButton;

    [Header("Colors")]
    [Tooltip("Color applied when the upgrade can be purchased.")]
    [SerializeField] private Color PurchasableColor = Color.white;

    [Tooltip("Color applied when the upgrade cannot currently be purchased.")]
    [SerializeField] private Color NotPurchasableColor = new Color(1f, 0.55f, 0.55f, 1f);

    [Tooltip("Color applied when the upgrade is already at maximum level.")]
    [SerializeField] private Color MaxedColor = new Color(0.5f, 1f, 0.5f, 1f);

    private UpgradeManager UpgradeManager;
    private UpgradeDefinition UpgradeDefinition;

    /// <summary>
    /// Initializes this entry with runtime references and the represented upgrade definition.
    /// </summary>
    public void Initialize(UpgradeManager UpgradeManagerReference, CurrencyWallet CurrencyWalletReference, UpgradeDefinition UpgradeDefinitionReference)
    {
        UpgradeManager = UpgradeManagerReference;
        UpgradeDefinition = UpgradeDefinitionReference;

        if (PurchaseButton != null)
        {
            PurchaseButton.onClick.RemoveListener(HandlePurchaseButtonClicked);
            PurchaseButton.onClick.AddListener(HandlePurchaseButtonClicked);
        }

        RefreshView();
    }

    /// <summary>
    /// Refreshes all texts, icon state and purchase availability.
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
        bool CanPurchase = !IsMaxed && UpgradeManager.CanPurchaseUpgrade(UpgradeDefinition);

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
            StateText.text = BuildStateText(IsMaxed, CanPurchase);
            StateText.color = GetStateColor(IsMaxed, CanPurchase);
        }

        if (PurchaseButton != null)
        {
            PurchaseButton.interactable = CanPurchase;
        }
    }

    /// <summary>
    /// Attempts to purchase the represented upgrade.
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
    /// Builds the cost text for the next upgrade level.
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

        return "Cost: " + NextCost.GetCost() + " " + NextCost.GetCurrencyType();
    }

    /// <summary>
    /// Builds a short effect preview text for the represented upgrade.
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
    /// Builds the current state label of the upgrade entry.
    /// </summary>
    private string BuildStateText(bool IsMaxed, bool CanPurchase)
    {
        if (IsMaxed)
        {
            return "MAXED";
        }

        return CanPurchase ? "Available" : "Not enough currency";
    }

    /// <summary>
    /// Returns the color used by the state label.
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
