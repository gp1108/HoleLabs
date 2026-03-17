using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central runtime service for upgrade ownership, purchases, stat evaluation and unlock rewards.
/// Gameplay systems should query this manager instead of storing upgrade logic locally.
/// </summary>
public sealed class UpgradeManager : MonoBehaviour
{
    [Serializable]
    private sealed class UpgradeLevelEntry
    {
        [Tooltip("Upgrade definition tracked by this entry.")]
        [SerializeField] private UpgradeDefinition Definition;

        [Tooltip("Current purchased level of the upgrade.")]
        [SerializeField] private int CurrentLevel = 0;

        /// <summary>
        /// Gets the upgrade definition tracked by this entry.
        /// </summary>
        public UpgradeDefinition GetDefinition()
        {
            return Definition;
        }

        /// <summary>
        /// Gets the current purchased level of the upgrade.
        /// </summary>
        public int GetCurrentLevel()
        {
            return CurrentLevel;
        }

        /// <summary>
        /// Sets the current purchased level of the upgrade.
        /// </summary>
        public void SetCurrentLevel(int level)
        {
            CurrentLevel = Mathf.Max(0, level);
        }
    }

    [Header("References")]
    [Tooltip("Wallet used to pay for upgrade purchases.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Header("Definitions")]
    [Tooltip("All upgrade definitions supported by this manager.")]
    [SerializeField] private List<UpgradeDefinition> UpgradeDefinitions = new();

    [Header("Runtime Debug")]
    [Tooltip("Optional serialized runtime levels useful during development and save loading tests.")]
    [SerializeField] private List<UpgradeLevelEntry> DebugRuntimeLevels = new();

    [Tooltip("Logs upgrade operations to the console.")]
    [SerializeField] private bool DebugLogs = false;

    private readonly Dictionary<string, UpgradeDefinition> DefinitionsById = new();
    private readonly Dictionary<string, int> LevelsById = new();
    private readonly HashSet<string> ActiveFeatureFlags = new();
    private readonly HashSet<string> ActiveVisualEffectIds = new();
    private readonly HashSet<ItemDefinition> ActiveUnlockedItems = new();

    /// <summary>
    /// Fired after an upgrade level changes.
    /// The first argument is the upgrade definition and the second argument is the new level.
    /// </summary>
    public event Action<UpgradeDefinition, int> OnUpgradeLevelChanged;

    /// <summary>
    /// Fired after any stat-affecting or reward-affecting upgrade change occurs.
    /// Systems that cache values can listen to this event and refresh themselves.
    /// </summary>
    public event Action OnUpgradeStateChanged;

    /// <summary>
    /// Initializes references and builds runtime caches.
    /// </summary>
    private void Awake()
    {
        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        RebuildDefinitionCache();
        RebuildRewardCache();
    }

    /// <summary>
    /// Gets the current level of the provided upgrade definition.
    /// </summary>
    public int GetUpgradeLevel(UpgradeDefinition upgradeDefinition)
    {
        if (upgradeDefinition == null)
        {
            return 0;
        }

        return GetUpgradeLevelById(upgradeDefinition.GetUpgradeId());
    }

    /// <summary>
    /// Gets the current level of the upgrade with the provided identifier.
    /// </summary>
    public int GetUpgradeLevelById(string upgradeId)
    {
        if (string.IsNullOrWhiteSpace(upgradeId))
        {
            return 0;
        }

        if (LevelsById.TryGetValue(upgradeId, out int level))
        {
            return level;
        }

        return 0;
    }

    /// <summary>
    /// Gets the definition registered with the provided upgrade identifier.
    /// </summary>
    public UpgradeDefinition GetUpgradeDefinition(string upgradeId)
    {
        if (string.IsNullOrWhiteSpace(upgradeId))
        {
            return null;
        }

        if (DefinitionsById.TryGetValue(upgradeId, out UpgradeDefinition definition))
        {
            return definition;
        }

        return null;
    }

    /// <summary>
    /// Checks whether the next level of the provided upgrade can be purchased.
    /// </summary>
    public bool CanPurchaseUpgrade(UpgradeDefinition upgradeDefinition)
    {
        if (upgradeDefinition == null || CurrencyWallet == null)
        {
            return false;
        }

        int currentLevel = GetUpgradeLevel(upgradeDefinition);

        if (currentLevel >= upgradeDefinition.GetMaxLevel())
        {
            return false;
        }

        int nextLevel = currentLevel + 1;
        UpgradeDefinition.UpgradeLevelCost nextCost = upgradeDefinition.GetCostForLevel(nextLevel);

        if (nextCost == null)
        {
            return false;
        }

        return CurrencyWallet.HasEnough(nextCost.GetCurrencyType(), nextCost.GetCost());
    }

    /// <summary>
    /// Attempts to purchase the next level of the provided upgrade.
    /// </summary>
    public bool TryPurchaseUpgrade(UpgradeDefinition upgradeDefinition)
    {
        if (upgradeDefinition == null || CurrencyWallet == null)
        {
            return false;
        }

        int currentLevel = GetUpgradeLevel(upgradeDefinition);

        if (currentLevel >= upgradeDefinition.GetMaxLevel())
        {
            Log("Upgrade is already at max level: " + upgradeDefinition.GetDisplayName());
            return false;
        }

        int nextLevel = currentLevel + 1;
        UpgradeDefinition.UpgradeLevelCost nextCost = upgradeDefinition.GetCostForLevel(nextLevel);

        if (nextCost == null)
        {
            Log("Missing configured cost for level " + nextLevel + " on upgrade " + upgradeDefinition.GetDisplayName());
            return false;
        }

        if (!CurrencyWallet.TrySpendCurrency(nextCost.GetCurrencyType(), nextCost.GetCost()))
        {
            Log("Not enough currency to purchase upgrade " + upgradeDefinition.GetDisplayName());
            return false;
        }

        SetUpgradeLevel(upgradeDefinition, nextLevel);
        Log("Purchased upgrade " + upgradeDefinition.GetDisplayName() + " to level " + nextLevel);
        return true;
    }

    /// <summary>
    /// Sets the exact level of the provided upgrade definition.
    /// Useful for debugging, progression grants or loading save data.
    /// </summary>
    public void SetUpgradeLevel(UpgradeDefinition upgradeDefinition, int level)
    {
        if (upgradeDefinition == null)
        {
            return;
        }

        string upgradeId = upgradeDefinition.GetUpgradeId();
        int clampedLevel = Mathf.Clamp(level, 0, upgradeDefinition.GetMaxLevel());

        LevelsById[upgradeId] = clampedLevel;
        SyncDebugRuntimeLevels(upgradeDefinition, clampedLevel);
        RebuildRewardCache();

        OnUpgradeLevelChanged?.Invoke(upgradeDefinition, clampedLevel);
        OnUpgradeStateChanged?.Invoke();
    }

    /// <summary>
    /// Returns the modified float stat after evaluating all purchased upgrades.
    /// </summary>
    public float GetModifiedFloatStat(UpgradeStatType statType, float baseValue)
    {
        float currentValue = baseValue;

        foreach (UpgradeDefinition definition in UpgradeDefinitions)
        {
            if (definition == null)
            {
                continue;
            }

            int currentLevel = GetUpgradeLevel(definition);

            if (currentLevel <= 0)
            {
                continue;
            }

            IReadOnlyList<UpgradeDefinition.StatModifierDefinition> modifiers = definition.GetStatModifiers();

            for (int index = 0; index < modifiers.Count; index++)
            {
                UpgradeDefinition.StatModifierDefinition modifier = modifiers[index];

                if (modifier == null || modifier.GetStatType() != statType)
                {
                    continue;
                }

                float modifierValue = modifier.EvaluateValue(currentLevel);
                currentValue = ApplyModifier(currentValue, modifier.GetModifierType(), modifierValue);
            }
        }

        return currentValue;
    }

    /// <summary>
    /// Returns the modified integer stat after evaluating all purchased upgrades.
    /// </summary>
    public int GetModifiedIntStat(UpgradeStatType statType, int baseValue)
    {
        float modifiedValue = GetModifiedFloatStat(statType, baseValue);
        return Mathf.RoundToInt(modifiedValue);
    }

    /// <summary>
    /// Checks whether a feature flag reward is currently unlocked.
    /// </summary>
    public bool IsFeatureUnlocked(string featureFlagId)
    {
        if (string.IsNullOrWhiteSpace(featureFlagId))
        {
            return false;
        }

        return ActiveFeatureFlags.Contains(featureFlagId);
    }

    /// <summary>
    /// Checks whether a visual effect reward is currently unlocked.
    /// </summary>
    public bool IsVisualEffectUnlocked(string visualEffectId)
    {
        if (string.IsNullOrWhiteSpace(visualEffectId))
        {
            return false;
        }

        return ActiveVisualEffectIds.Contains(visualEffectId);
    }

    /// <summary>
    /// Checks whether an item reward is currently unlocked.
    /// </summary>
    public bool IsItemUnlocked(ItemDefinition itemDefinition)
    {
        if (itemDefinition == null)
        {
            return false;
        }

        return ActiveUnlockedItems.Contains(itemDefinition);
    }

    /// <summary>
    /// Rebuilds the runtime definition and level caches.
    /// </summary>
    private void RebuildDefinitionCache()
    {
        DefinitionsById.Clear();

        foreach (UpgradeDefinition definition in UpgradeDefinitions)
        {
            if (definition == null)
            {
                continue;
            }

            string upgradeId = definition.GetUpgradeId();

            if (string.IsNullOrWhiteSpace(upgradeId))
            {
                continue;
            }

            DefinitionsById[upgradeId] = definition;

            if (!LevelsById.ContainsKey(upgradeId))
            {
                LevelsById[upgradeId] = 0;
            }
        }

        for (int index = 0; index < DebugRuntimeLevels.Count; index++)
        {
            UpgradeLevelEntry entry = DebugRuntimeLevels[index];

            if (entry == null || entry.GetDefinition() == null)
            {
                continue;
            }

            string upgradeId = entry.GetDefinition().GetUpgradeId();

            if (string.IsNullOrWhiteSpace(upgradeId))
            {
                continue;
            }

            LevelsById[upgradeId] = Mathf.Clamp(entry.GetCurrentLevel(), 0, entry.GetDefinition().GetMaxLevel());
        }
    }

    /// <summary>
    /// Rebuilds all non numeric unlock caches from the current purchased levels.
    /// </summary>
    private void RebuildRewardCache()
    {
        ActiveFeatureFlags.Clear();
        ActiveVisualEffectIds.Clear();
        ActiveUnlockedItems.Clear();

        foreach (UpgradeDefinition definition in UpgradeDefinitions)
        {
            if (definition == null)
            {
                continue;
            }

            int currentLevel = GetUpgradeLevel(definition);

            if (currentLevel <= 0)
            {
                continue;
            }

            IReadOnlyList<UpgradeDefinition.UnlockRewardDefinition> rewards = definition.GetUnlockRewards();

            for (int index = 0; index < rewards.Count; index++)
            {
                UpgradeDefinition.UnlockRewardDefinition reward = rewards[index];

                if (reward == null)
                {
                    continue;
                }

                if (currentLevel < reward.GetRequiredLevel())
                {
                    continue;
                }

                switch (reward.GetRewardType())
                {
                    case UpgradeDefinition.UnlockRewardDefinition.UnlockRewardType.FeatureFlag:
                        if (!string.IsNullOrWhiteSpace(reward.GetRewardId()))
                        {
                            ActiveFeatureFlags.Add(reward.GetRewardId());
                        }
                        break;

                    case UpgradeDefinition.UnlockRewardDefinition.UnlockRewardType.VisualEffect:
                        if (!string.IsNullOrWhiteSpace(reward.GetRewardId()))
                        {
                            ActiveVisualEffectIds.Add(reward.GetRewardId());
                        }
                        break;

                    case UpgradeDefinition.UnlockRewardDefinition.UnlockRewardType.Item:
                        if (reward.GetItemDefinition() != null)
                        {
                            ActiveUnlockedItems.Add(reward.GetItemDefinition());
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Synchronizes the optional debug runtime list with a new upgrade level.
    /// </summary>
    private void SyncDebugRuntimeLevels(UpgradeDefinition definition, int level)
    {
        for (int index = 0; index < DebugRuntimeLevels.Count; index++)
        {
            UpgradeLevelEntry entry = DebugRuntimeLevels[index];

            if (entry == null || entry.GetDefinition() != definition)
            {
                continue;
            }

            entry.SetCurrentLevel(level);
            return;
        }
    }

    /// <summary>
    /// Applies a numeric modifier operation to the provided value.
    /// </summary>
    private float ApplyModifier(float currentValue, UpgradeModifierType modifierType, float modifierValue)
    {
        switch (modifierType)
        {
            case UpgradeModifierType.Add:
                return currentValue + modifierValue;

            case UpgradeModifierType.Subtract:
                return currentValue - modifierValue;

            case UpgradeModifierType.Multiply:
                return currentValue * modifierValue;

            case UpgradeModifierType.Divide:
                if (Mathf.Approximately(modifierValue, 0f))
                {
                    return currentValue;
                }

                return currentValue / modifierValue;

            case UpgradeModifierType.Override:
                return modifierValue;

            default:
                return currentValue;
        }
    }

    /// <summary>
    /// Logs upgrade messages if debug logging is enabled.
    /// </summary>
    private void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[UpgradeManager] " + message);
    }
}
