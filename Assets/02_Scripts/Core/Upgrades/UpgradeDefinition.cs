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

        public UpgradeStatType GetStatType()
        {
            return StatType;
        }

        public UpgradeModifierType GetModifierType()
        {
            return ModifierType;
        }

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

        public UnlockRewardType GetRewardType()
        {
            return RewardType;
        }

        public string GetRewardId()
        {
            return RewardId;
        }

        public ItemDefinition GetItemDefinition()
        {
            return ItemDefinition;
        }

        public int GetRequiredLevel()
        {
            return Mathf.Max(1, RequiredLevel);
        }
    }

    [Header("Identity")]
    [SerializeField] private string UpgradeId;
    [SerializeField] private string DisplayName;
    [TextArea]
    [SerializeField] private string Description;
    [SerializeField] private Sprite Icon;

    [Header("Leveling")]
    [SerializeField] private int MaxLevel = 1;
    [SerializeField] private List<UpgradeLevelCost> LevelCosts = new();

    [Header("Stat Modifiers")]
    [SerializeField] private List<StatModifierDefinition> StatModifiers = new();

    [Header("Unlock Rewards")]
    [SerializeField] private List<UnlockRewardDefinition> UnlockRewards = new();

    public string GetUpgradeId() => UpgradeId;
    public string GetDisplayName() => DisplayName;
    public string GetDescription() => Description;
    public Sprite GetIcon() => Icon;
    public int GetMaxLevel() => Mathf.Max(1, MaxLevel);

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

    public IReadOnlyList<StatModifierDefinition> GetStatModifiers()
    {
        return StatModifiers;
    }

    public IReadOnlyList<UnlockRewardDefinition> GetUnlockRewards()
    {
        return UnlockRewards;
    }
}