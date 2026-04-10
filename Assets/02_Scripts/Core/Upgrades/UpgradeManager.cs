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
        [SerializeField] private UpgradeDefinition Definition;
        [SerializeField] private int CurrentLevel = 0;

        public UpgradeDefinition GetDefinition() => Definition;
        public int GetCurrentLevel() => CurrentLevel;
        public void SetCurrentLevel(int Level) => CurrentLevel = Mathf.Max(0, Level);
    }

    [Header("References")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Header("Definitions")]
    [SerializeField] private List<UpgradeDefinition> UpgradeDefinitions = new();

    [Header("Runtime Debug")]
    [SerializeField] private List<UpgradeLevelEntry> DebugRuntimeLevels = new();
    [SerializeField] private bool DebugLogs = false;

    private readonly Dictionary<string, UpgradeDefinition> DefinitionsById = new();
    private readonly Dictionary<string, int> LevelsById = new();
    private readonly HashSet<string> ActiveFeatureFlags = new();
    private readonly HashSet<string> ActiveVisualEffectIds = new();
    private readonly HashSet<ItemDefinition> ActiveUnlockedItems = new();

    public event Action<UpgradeDefinition, int> OnUpgradeLevelChanged;
    public event Action OnUpgradeStateChanged;

    private void Awake()
    {
        if (CurrencyWallet == null)
        {
            CurrencyWallet = FindFirstObjectByType<CurrencyWallet>();
        }

        RebuildDefinitionCache();
        RebuildRewardCache();
    }

    public IReadOnlyList<UpgradeDefinition> GetAllUpgradeDefinitions()
    {
        return UpgradeDefinitions;
    }

    public int GetUpgradeLevel(UpgradeDefinition UpgradeDefinition)
    {
        if (UpgradeDefinition == null)
        {
            return 0;
        }

        return GetUpgradeLevelById(UpgradeDefinition.GetUpgradeId());
    }

    public int GetUpgradeLevelById(string UpgradeId)
    {
        if (string.IsNullOrWhiteSpace(UpgradeId))
        {
            return 0;
        }

        if (LevelsById.TryGetValue(UpgradeId, out int Level))
        {
            return Level;
        }

        return 0;
    }

    public UpgradeDefinition GetUpgradeDefinition(string UpgradeId)
    {
        if (string.IsNullOrWhiteSpace(UpgradeId))
        {
            return null;
        }

        if (DefinitionsById.TryGetValue(UpgradeId, out UpgradeDefinition Definition))
        {
            return Definition;
        }

        return null;
    }

    public float GetModifiedOreFloatStat(UpgradeStatType StatType, string OreId, float BaseValue)
    {
        float CurrentValue = BaseValue;

        foreach (UpgradeDefinition Definition in UpgradeDefinitions)
        {
            if (Definition == null)
            {
                continue;
            }

            int CurrentLevel = GetUpgradeLevel(Definition);

            if (CurrentLevel <= 0)
            {
                continue;
            }

            IReadOnlyList<UpgradeDefinition.StatModifierDefinition> Modifiers = Definition.GetStatModifiers();

            for (int Index = 0; Index < Modifiers.Count; Index++)
            {
                UpgradeDefinition.StatModifierDefinition Modifier = Modifiers[Index];

                if (Modifier == null || Modifier.GetStatType() != StatType)
                {
                    continue;
                }

                if (!Modifier.AppliesToOre(OreId))
                {
                    continue;
                }

                float ModifierValue = Modifier.EvaluateValue(CurrentLevel);
                CurrentValue = ApplyModifier(CurrentValue, Modifier.GetModifierType(), ModifierValue);
            }
        }

        return CurrentValue;
    }

    public int GetModifiedOreIntStat(UpgradeStatType StatType, string OreId, int BaseValue)
    {
        float ModifiedValue = GetModifiedOreFloatStat(StatType, OreId, BaseValue);
        return Mathf.RoundToInt(ModifiedValue);
    }

    public bool CanPurchaseUpgrade(UpgradeDefinition UpgradeDefinition)
    {
        if (UpgradeDefinition == null || CurrencyWallet == null)
        {
            return false;
        }

        int CurrentLevel = GetUpgradeLevel(UpgradeDefinition);

        if (CurrentLevel >= UpgradeDefinition.GetMaxLevel())
        {
            return false;
        }

        int NextLevel = CurrentLevel + 1;
        UpgradeDefinition.UpgradeLevelCost NextCost = UpgradeDefinition.GetCostForLevel(NextLevel);

        if (NextCost == null)
        {
            return false;
        }

        return CurrencyWallet.HasEnough(NextCost.GetCurrencyType(), NextCost.GetCost());
    }

    public bool TryPurchaseUpgrade(UpgradeDefinition UpgradeDefinition)
    {
        if (UpgradeDefinition == null || CurrencyWallet == null)
        {
            return false;
        }

        int CurrentLevel = GetUpgradeLevel(UpgradeDefinition);

        if (CurrentLevel >= UpgradeDefinition.GetMaxLevel())
        {
            Log("Upgrade is already at max level: " + UpgradeDefinition.GetDisplayName());
            return false;
        }

        int NextLevel = CurrentLevel + 1;
        UpgradeDefinition.UpgradeLevelCost NextCost = UpgradeDefinition.GetCostForLevel(NextLevel);

        if (NextCost == null)
        {
            Log("Missing configured cost for level " + NextLevel + " on upgrade " + UpgradeDefinition.GetDisplayName());
            return false;
        }

        if (!CurrencyWallet.TrySpendCurrency(NextCost.GetCurrencyType(), NextCost.GetCost()))
        {
            Log("Not enough currency to purchase upgrade " + UpgradeDefinition.GetDisplayName());
            return false;
        }

        SetUpgradeLevel(UpgradeDefinition, NextLevel);
        Log("Purchased upgrade " + UpgradeDefinition.GetDisplayName() + " to level " + NextLevel);
        return true;
    }

    public void SetUpgradeLevel(UpgradeDefinition UpgradeDefinition, int Level)
    {
        if (UpgradeDefinition == null)
        {
            return;
        }

        string UpgradeId = UpgradeDefinition.GetUpgradeId();
        int ClampedLevel = Mathf.Clamp(Level, 0, UpgradeDefinition.GetMaxLevel());

        LevelsById[UpgradeId] = ClampedLevel;
        SyncDebugRuntimeLevels(UpgradeDefinition, ClampedLevel);
        RebuildRewardCache();

        OnUpgradeLevelChanged?.Invoke(UpgradeDefinition, ClampedLevel);
        OnUpgradeStateChanged?.Invoke();
    }

    public float GetModifiedFloatStat(UpgradeStatType StatType, float BaseValue)
    {
        float CurrentValue = BaseValue;

        foreach (UpgradeDefinition Definition in UpgradeDefinitions)
        {
            if (Definition == null)
            {
                continue;
            }

            int CurrentLevel = GetUpgradeLevel(Definition);

            if (CurrentLevel <= 0)
            {
                continue;
            }

            IReadOnlyList<UpgradeDefinition.StatModifierDefinition> Modifiers = Definition.GetStatModifiers();

            for (int Index = 0; Index < Modifiers.Count; Index++)
            {
                UpgradeDefinition.StatModifierDefinition Modifier = Modifiers[Index];

                if (Modifier == null || Modifier.GetStatType() != StatType)
                {
                    continue;
                }

                if (!Modifier.AppliesToOre(null))
                {
                    continue;
                }

                float ModifierValue = Modifier.EvaluateValue(CurrentLevel);
                CurrentValue = ApplyModifier(CurrentValue, Modifier.GetModifierType(), ModifierValue);
            }
        }

        return CurrentValue;
    }

    public int GetModifiedIntStat(UpgradeStatType StatType, int BaseValue)
    {
        float ModifiedValue = GetModifiedFloatStat(StatType, BaseValue);
        return Mathf.RoundToInt(ModifiedValue);
    }

    public bool IsFeatureUnlocked(string FeatureFlagId)
    {
        if (string.IsNullOrWhiteSpace(FeatureFlagId))
        {
            return false;
        }

        return ActiveFeatureFlags.Contains(FeatureFlagId);
    }

    public bool IsVisualEffectUnlocked(string VisualEffectId)
    {
        if (string.IsNullOrWhiteSpace(VisualEffectId))
        {
            return false;
        }

        return ActiveVisualEffectIds.Contains(VisualEffectId);
    }

    public bool IsItemUnlocked(ItemDefinition ItemDefinition)
    {
        if (ItemDefinition == null)
        {
            return false;
        }

        return ActiveUnlockedItems.Contains(ItemDefinition);
    }

    private void RebuildDefinitionCache()
    {
        DefinitionsById.Clear();

        foreach (UpgradeDefinition Definition in UpgradeDefinitions)
        {
            if (Definition == null)
            {
                continue;
            }

            string UpgradeId = Definition.GetUpgradeId();

            if (string.IsNullOrWhiteSpace(UpgradeId))
            {
                continue;
            }

            DefinitionsById[UpgradeId] = Definition;

            if (!LevelsById.ContainsKey(UpgradeId))
            {
                LevelsById[UpgradeId] = 0;
            }
        }

        for (int Index = 0; Index < DebugRuntimeLevels.Count; Index++)
        {
            UpgradeLevelEntry Entry = DebugRuntimeLevels[Index];

            if (Entry == null || Entry.GetDefinition() == null)
            {
                continue;
            }

            string UpgradeId = Entry.GetDefinition().GetUpgradeId();

            if (string.IsNullOrWhiteSpace(UpgradeId))
            {
                continue;
            }

            LevelsById[UpgradeId] = Mathf.Clamp(Entry.GetCurrentLevel(), 0, Entry.GetDefinition().GetMaxLevel());
        }
    }

    private void RebuildRewardCache()
    {
        ActiveFeatureFlags.Clear();
        ActiveVisualEffectIds.Clear();
        ActiveUnlockedItems.Clear();

        foreach (UpgradeDefinition Definition in UpgradeDefinitions)
        {
            if (Definition == null)
            {
                continue;
            }

            int CurrentLevel = GetUpgradeLevel(Definition);

            if (CurrentLevel <= 0)
            {
                continue;
            }

            IReadOnlyList<UpgradeDefinition.UnlockRewardDefinition> Rewards = Definition.GetUnlockRewards();

            for (int Index = 0; Index < Rewards.Count; Index++)
            {
                UpgradeDefinition.UnlockRewardDefinition Reward = Rewards[Index];

                if (Reward == null)
                {
                    continue;
                }

                if (CurrentLevel < Reward.GetRequiredLevel())
                {
                    continue;
                }

                switch (Reward.GetRewardType())
                {
                    case UpgradeDefinition.UnlockRewardDefinition.UnlockRewardType.FeatureFlag:
                        if (!string.IsNullOrWhiteSpace(Reward.GetRewardId()))
                        {
                            ActiveFeatureFlags.Add(Reward.GetRewardId());
                        }
                        break;

                    case UpgradeDefinition.UnlockRewardDefinition.UnlockRewardType.VisualEffect:
                        if (!string.IsNullOrWhiteSpace(Reward.GetRewardId()))
                        {
                            ActiveVisualEffectIds.Add(Reward.GetRewardId());
                        }
                        break;

                    case UpgradeDefinition.UnlockRewardDefinition.UnlockRewardType.Item:
                        if (Reward.GetItemDefinition() != null)
                        {
                            ActiveUnlockedItems.Add(Reward.GetItemDefinition());
                        }
                        break;
                }
            }
        }
    }

    private void SyncDebugRuntimeLevels(UpgradeDefinition UpgradeDefinition, int Level)
    {
        for (int Index = 0; Index < DebugRuntimeLevels.Count; Index++)
        {
            UpgradeLevelEntry Entry = DebugRuntimeLevels[Index];

            if (Entry == null || Entry.GetDefinition() != UpgradeDefinition)
            {
                continue;
            }

            Entry.SetCurrentLevel(Level);
            return;
        }
    }

    private float ApplyModifier(float CurrentValue, UpgradeModifierType ModifierType, float ModifierValue)
    {
        switch (ModifierType)
        {
            case UpgradeModifierType.Add:
                return CurrentValue + ModifierValue;

            case UpgradeModifierType.Subtract:
                return CurrentValue - ModifierValue;

            case UpgradeModifierType.Multiply:
                return CurrentValue * ModifierValue;

            case UpgradeModifierType.Divide:
                if (Mathf.Approximately(ModifierValue, 0f))
                {
                    return CurrentValue;
                }

                return CurrentValue / ModifierValue;

            case UpgradeModifierType.Override:
                return ModifierValue;

            default:
                return CurrentValue;
        }
    }

    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[UpgradeManager] " + Message, this);
    }
}