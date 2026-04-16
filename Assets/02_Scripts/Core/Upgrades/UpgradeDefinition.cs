using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "UpgradeDefinition_", menuName = "Game/Upgrades/Upgrade Definition")]
public sealed class UpgradeDefinition : ScriptableObject
{
    [Serializable]
    public sealed class UpgradeLevelCost
    {
        [Tooltip("Currency required to purchase this level.")]
        [SerializeField] private CurrencyWallet.CurrencyType CurrencyType = CurrencyWallet.CurrencyType.Research;

        [Tooltip("Amount required to purchase this level.")]
        [SerializeField] private float Cost = 100f;

        /// <summary>
        /// Gets the currency type used by this level cost.
        /// </summary>
        public CurrencyWallet.CurrencyType GetCurrencyType()
        {
            return CurrencyType;
        }

        /// <summary>
        /// Gets the numeric amount required by this level cost.
        /// </summary>
        public float GetCost()
        {
            return CurrencyMath.RoundCurrency(Mathf.Max(0f, Cost));
        }
    }

    [Serializable]
    public sealed class StatModifierDefinition
    {
        [Tooltip("Stat affected by this modifier.")]
        [SerializeField] private UpgradeStatType StatType = UpgradeStatType.None;

        [Tooltip("Operation applied to the target stat.")]
        [SerializeField] private UpgradeModifierType ModifierType = UpgradeModifierType.Add;

        [Tooltip("Base value applied by this modifier.")]
        [SerializeField] private float BaseValue = 0f;

        [Tooltip("Extra value added to the modifier for each purchased level.")]
        [SerializeField] private float ValuePerLevel = 0f;

        [Tooltip("If true, level zero contributes nothing and level one starts at BaseValue.")]
        [SerializeField] private bool StartApplyingAtLevelOne = true;

        [Tooltip("Optional ore id filter. Leave empty to apply this modifier globally.")]
        [SerializeField] private string TargetOreId;

        /// <summary>
        /// Gets the optional ore id restriction used by this modifier.
        /// </summary>
        public string GetTargetOreId()
        {
            return TargetOreId;
        }

        /// <summary>
        /// Returns whether this modifier applies to the provided ore id.
        /// </summary>
        public bool AppliesToOre(string OreId)
        {
            if (string.IsNullOrWhiteSpace(TargetOreId))
            {
                return true;
            }

            return string.Equals(TargetOreId, OreId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the stat affected by this modifier.
        /// </summary>
        public UpgradeStatType GetStatType()
        {
            return StatType;
        }

        /// <summary>
        /// Gets the modifier operation applied to the stat.
        /// </summary>
        public UpgradeModifierType GetModifierType()
        {
            return ModifierType;
        }

        /// <summary>
        /// Evaluates the modifier value for the provided current level.
        /// </summary>
        public float EvaluateValue(int CurrentLevel)
        {
            int EffectiveLevel = Mathf.Max(0, CurrentLevel);

            if (StartApplyingAtLevelOne)
            {
                if (EffectiveLevel <= 0)
                {
                    return 0f;
                }

                return BaseValue + (ValuePerLevel * (EffectiveLevel - 1));
            }

            return BaseValue + (ValuePerLevel * EffectiveLevel);
        }
    }

    [Serializable]
    public sealed class UnlockRewardDefinition
    {
        public enum UnlockRewardType
        {
            None = 0,
            FeatureFlag = 1,
            VisualEffect = 2,
            Item = 3
        }

        [Tooltip("Type of unlock granted by this reward.")]
        [SerializeField] private UnlockRewardType RewardType = UnlockRewardType.FeatureFlag;

        [Tooltip("Unique identifier used by gameplay systems to query whether this reward is unlocked.")]
        [SerializeField] private string RewardId;

        [Tooltip("Optional item definition granted or unlocked by this reward.")]
        [SerializeField] private ItemDefinition ItemDefinition;

        [Tooltip("Level at which this reward becomes active.")]
        [SerializeField] private int RequiredLevel = 1;

        /// <summary>
        /// Gets the reward type granted by this unlock.
        /// </summary>
        public UnlockRewardType GetRewardType()
        {
            return RewardType;
        }

        /// <summary>
        /// Gets the unique reward identifier.
        /// </summary>
        public string GetRewardId()
        {
            return RewardId;
        }

        /// <summary>
        /// Gets the item definition associated with this reward, if any.
        /// </summary>
        public ItemDefinition GetItemDefinition()
        {
            return ItemDefinition;
        }

        /// <summary>
        /// Gets the minimum purchased level required for this reward to become active.
        /// </summary>
        public int GetRequiredLevel()
        {
            return Mathf.Max(1, RequiredLevel);
        }
    }

    [Serializable]
    public sealed class UpgradePrerequisiteDefinition
    {
        [Tooltip("Upgrade asset that must be owned before this upgrade can be purchased.")]
        [SerializeField] private UpgradeDefinition RequiredUpgradeDefinition;

        [Tooltip("Minimum level required on the referenced upgrade.")]
        [SerializeField] private int RequiredLevel = 1;

        /// <summary>
        /// Gets the referenced upgrade definition required by this prerequisite.
        /// </summary>
        public UpgradeDefinition GetRequiredUpgradeDefinition()
        {
            return RequiredUpgradeDefinition;
        }

        /// <summary>
        /// Gets the minimum required level for the referenced upgrade.
        /// </summary>
        public int GetRequiredLevel()
        {
            return Mathf.Max(1, RequiredLevel);
        }
    }

    [Header("Identity")]
    [Tooltip("Unique runtime identifier used by systems to query this upgrade.")]
    [SerializeField] private string UpgradeId;

    [Tooltip("User-facing title shown in the upgrade UI.")]
    [SerializeField] private string DisplayName;

    [Tooltip("User-facing description shown in the upgrade UI.")]
    [TextArea]
    [SerializeField] private string Description;

    [Tooltip("Optional icon shown in the upgrade UI.")]
    [SerializeField] private Sprite Icon;

    [Header("Progression Metadata")]
    [Tooltip("Logical shop identifier used by UI panels to group this upgrade into a specific store.")]
    [SerializeField] private string ShopId;

    [Tooltip("Optional prerequisite upgrades required before this upgrade can be purchased.")]
    [SerializeField] private List<UpgradePrerequisiteDefinition> Prerequisites = new();

    [Header("Leveling")]
    [Tooltip("Maximum level that can be purchased for this upgrade.")]
    [SerializeField] private int MaxLevel = 1;

    [Tooltip("Configured purchase cost per level.")]
    [SerializeField] private List<UpgradeLevelCost> LevelCosts = new();

    [Header("Stat Modifiers")]
    [Tooltip("Stat changes applied by this upgrade while it has purchased levels.")]
    [SerializeField] private List<StatModifierDefinition> StatModifiers = new();

    [Header("Unlock Rewards")]
    [Tooltip("Non-stat rewards unlocked by reaching specific levels on this upgrade.")]
    [SerializeField] private List<UnlockRewardDefinition> UnlockRewards = new();

    /// <summary>
    /// Gets the unique runtime identifier for this upgrade.
    /// </summary>
    public string GetUpgradeId()
    {
        return UpgradeId;
    }

    /// <summary>
    /// Gets the display name shown in UI.
    /// </summary>
    public string GetDisplayName()
    {
        return DisplayName;
    }

    /// <summary>
    /// Gets the display description shown in UI.
    /// </summary>
    public string GetDescription()
    {
        return Description;
    }

    /// <summary>
    /// Gets the icon shown in UI.
    /// </summary>
    public Sprite GetIcon()
    {
        return Icon;
    }

    /// <summary>
    /// Gets the logical shop identifier used to group this upgrade in a specific panel.
    /// </summary>
    public string GetShopId()
    {
        return ShopId;
    }

    /// <summary>
    /// Gets the configured maximum purchasable level.
    /// </summary>
    public int GetMaxLevel()
    {
        return Mathf.Max(1, MaxLevel);
    }

    /// <summary>
    /// Gets the configured cost definition for the provided target level.
    /// </summary>
    public UpgradeLevelCost GetCostForLevel(int Level)
    {
        int ClampedLevel = Mathf.Clamp(Level, 1, GetMaxLevel());
        int CostIndex = ClampedLevel - 1;

        if (CostIndex < 0 || CostIndex >= LevelCosts.Count)
        {
            return null;
        }

        return LevelCosts[CostIndex];
    }

    /// <summary>
    /// Gets all stat modifiers configured for this upgrade.
    /// </summary>
    public IReadOnlyList<StatModifierDefinition> GetStatModifiers()
    {
        return StatModifiers;
    }

    /// <summary>
    /// Gets all unlock rewards configured for this upgrade.
    /// </summary>
    public IReadOnlyList<UnlockRewardDefinition> GetUnlockRewards()
    {
        return UnlockRewards;
    }

    /// <summary>
    /// Gets all prerequisite requirements configured for this upgrade.
    /// </summary>
    public IReadOnlyList<UpgradePrerequisiteDefinition> GetPrerequisites()
    {
        return Prerequisites;
    }
}