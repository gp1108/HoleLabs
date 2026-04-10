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
    [SerializeField] private Image IconImage;
    [SerializeField] private TMP_Text NameText;
    [SerializeField] private TMP_Text DescriptionText;
    [SerializeField] private TMP_Text LevelText;
    [SerializeField] private TMP_Text CostText;
    [SerializeField] private TMP_Text EffectPreviewText;
    [SerializeField] private TMP_Text StateText;
    [SerializeField] private Button PurchaseButton;

    [Header("Colors")]
    [SerializeField] private Color PurchasableColor = Color.white;
    [SerializeField] private Color NotPurchasableColor = new Color(1f, 0.55f, 0.55f, 1f);
    [SerializeField] private Color MaxedColor = new Color(0.5f, 1f, 0.5f, 1f);

    private UpgradeManager UpgradeManager;
    private UpgradeDefinition UpgradeDefinition;

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

    private void HandlePurchaseButtonClicked()
    {
        if (UpgradeManager == null || UpgradeDefinition == null)
        {
            return;
        }

        UpgradeManager.TryPurchaseUpgrade(UpgradeDefinition);
    }

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

    private string BuildStateText(bool IsMaxed, bool CanPurchase)
    {
        if (IsMaxed)
        {
            return "MAXED";
        }

        return CanPurchase ? "Available" : "Not enough currency";
    }

    private Color GetStateColor(bool IsMaxed, bool CanPurchase)
    {
        if (IsMaxed)
        {
            return MaxedColor;
        }

        return CanPurchase ? PurchasableColor : NotPurchasableColor;
    }
}