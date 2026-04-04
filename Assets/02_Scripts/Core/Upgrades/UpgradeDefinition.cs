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
        [SerializeField] private int Cost = 100;



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
        public int GetCost()
        {
            return Mathf.Max(0, Cost);
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

        public string GetTargetOreId()
        {
            return TargetOreId;
        }

        public bool AppliesToOre(string OreId)
        {
            if (string.IsNullOrWhiteSpace(TargetOreId))
            {
                return true;
            }

            return string.Equals(TargetOreId, OreId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the target stat affected by this modifier.
        /// </summary>
        public UpgradeStatType GetStatType()
        {
            return StatType;
        }

        /// <summary>
        /// Gets the modifier operation used by this modifier.
        /// </summary>
        public UpgradeModifierType GetModifierType()
        {
            return ModifierType;
        }

        /// <summary>
        /// Evaluates the modifier numeric value for the provided upgrade level.
        /// </summary>
        public float EvaluateValue(int currentLevel)
        {
            int effectiveLevel = Mathf.Max(0, currentLevel);

            if (StartApplyingAtLevelOne)
            {
                if (effectiveLevel <= 0)
                {
                    return 0f;
                }

                return BaseValue + (ValuePerLevel * (effectiveLevel - 1));
            }

            return BaseValue + (ValuePerLevel * effectiveLevel);
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
        /// Gets the reward category used by this unlock reward.
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
        /// Gets the optional item definition referenced by this reward.
        /// </summary>
        public ItemDefinition GetItemDefinition()
        {
            return ItemDefinition;
        }

        /// <summary>
        /// Gets the minimum level required to activate this reward.
        /// </summary>
        public int GetRequiredLevel()
        {
            return Mathf.Max(1, RequiredLevel);
        }
    }

    [Header("Identity")]
    [Tooltip("Unique identifier of this upgrade.")]
    [SerializeField] private string UpgradeId;

    [Tooltip("Display name shown in UI.")]
    [SerializeField] private string DisplayName;

    [Tooltip("Description shown in UI.")]
    [TextArea]
    [SerializeField] private string Description;

    [Tooltip("Optional icon displayed by the upgrade UI.")]
    [SerializeField] private Sprite Icon;

    [Header("Leveling")]
    [Tooltip("Maximum level supported by this upgrade.")]
    [SerializeField] private int MaxLevel = 1;

    [Tooltip("Costs configured for each purchasable level. Element 0 represents level 1.")]
    [SerializeField] private List<UpgradeLevelCost> LevelCosts = new();

    [Header("Stat Modifiers")]
    [Tooltip("Numeric stat modifiers applied by this upgrade.")]
    [SerializeField] private List<StatModifierDefinition> StatModifiers = new();

    [Header("Unlock Rewards")]
    [Tooltip("Non numeric rewards activated by this upgrade, such as feature flags, visuals or items.")]
    [SerializeField] private List<UnlockRewardDefinition> UnlockRewards = new();

    /// <summary>
    /// Gets the unique identifier of this upgrade.
    /// </summary>
    public string GetUpgradeId()
    {
        return UpgradeId;
    }

    /// <summary>
    /// Gets the display name of this upgrade.
    /// </summary>
    public string GetDisplayName()
    {
        return DisplayName;
    }

    /// <summary>
    /// Gets the UI description of this upgrade.
    /// </summary>
    public string GetDescription()
    {
        return Description;
    }

    /// <summary>
    /// Gets the icon used by the UI for this upgrade.
    /// </summary>
    public Sprite GetIcon()
    {
        return Icon;
    }

    /// <summary>
    /// Gets the maximum level supported by this upgrade.
    /// </summary>
    public int GetMaxLevel()
    {
        return Mathf.Max(1, MaxLevel);
    }

    /// <summary>
    /// Gets the configured level cost for the provided level.
    /// Level one maps to index zero in the serialized list.
    /// </summary>
    public UpgradeLevelCost GetCostForLevel(int level)
    {
        int clampedLevel = Mathf.Clamp(level, 1, GetMaxLevel());
        int costIndex = clampedLevel - 1;

        if (costIndex < 0 || costIndex >= LevelCosts.Count)
        {
            return null;
        }

        return LevelCosts[costIndex];
    }

    /// <summary>
    /// Gets the numeric modifiers configured for this upgrade.
    /// </summary>
    public IReadOnlyList<StatModifierDefinition> GetStatModifiers()
    {
        return StatModifiers;
    }

    /// <summary>
    /// Gets the non numeric unlock rewards configured for this upgrade.
    /// </summary>
    public IReadOnlyList<UnlockRewardDefinition> GetUnlockRewards()
    {
        return UnlockRewards;
    }
}
