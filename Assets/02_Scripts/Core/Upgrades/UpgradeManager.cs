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
    public sealed class UpgradeSaveEntry
    {
        [Tooltip("Upgrade id saved in the slot.")]
        [SerializeField] private string UpgradeId;

        [Tooltip("Purchased level saved for this upgrade.")]
        [SerializeField] private int Level;

        /// <summary>
        /// Creates one upgrade save entry.
        /// </summary>
        public UpgradeSaveEntry(string UpgradeIdValue, int LevelValue)
        {
            UpgradeId = UpgradeIdValue;
            Level = Mathf.Max(0, LevelValue);
        }

        /// <summary>
        /// Gets the saved upgrade id.
        /// </summary>
        public string GetUpgradeId()
        {
            return UpgradeId;
        }

        /// <summary>
        /// Gets the saved purchased level.
        /// </summary>
        public int GetLevel()
        {
            return Mathf.Max(0, Level);
        }
    }

    /// <summary>
    /// Creates a compact save snapshot of all currently owned upgrade levels.
    /// Only upgrades above level zero are stored.
    /// </summary>
    public List<UpgradeSaveEntry> CreateSaveEntries()
    {
        List<UpgradeSaveEntry> Result = new List<UpgradeSaveEntry>();

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

            int CurrentLevel = GetUpgradeLevel(Definition);

            if (CurrentLevel <= 0)
            {
                continue;
            }

            Result.Add(new UpgradeSaveEntry(UpgradeId, CurrentLevel));
        }

        return Result;
    }

    /// <summary>
    /// Applies a full saved upgrade snapshot.
    /// Known upgrades are reset first and then restored from the saved entries.
    /// </summary>
    /// <param name="Entries">Saved upgrade entries.</param>
    public void ApplySaveEntries(List<UpgradeSaveEntry> Entries)
    {
        for (int Index = 0; Index < UpgradeDefinitions.Count; Index++)
        {
            UpgradeDefinition Definition = UpgradeDefinitions[Index];

            if (Definition == null)
            {
                continue;
            }

            string UpgradeId = Definition.GetUpgradeId();

            if (string.IsNullOrWhiteSpace(UpgradeId))
            {
                continue;
            }

            LevelsById[UpgradeId] = 0;
            SyncDebugRuntimeLevels(Definition, 0);
        }

        if (Entries != null)
        {
            for (int Index = 0; Index < Entries.Count; Index++)
            {
                UpgradeSaveEntry Entry = Entries[Index];

                if (Entry == null || string.IsNullOrWhiteSpace(Entry.GetUpgradeId()))
                {
                    continue;
                }

                UpgradeDefinition Definition = GetUpgradeDefinition(Entry.GetUpgradeId());

                if (Definition == null)
                {
                    continue;
                }

                int ClampedLevel = Mathf.Clamp(Entry.GetLevel(), 0, Definition.GetMaxLevel());
                LevelsById[Definition.GetUpgradeId()] = ClampedLevel;
                SyncDebugRuntimeLevels(Definition, ClampedLevel);
                OnUpgradeLevelChanged?.Invoke(Definition, ClampedLevel);
            }
        }

        RebuildRewardCache();
        OnUpgradeStateChanged?.Invoke();
    }

    [Serializable]
    private sealed class UpgradeLevelEntry
    {
        [Tooltip("Upgrade definition represented by this runtime debug entry.")]
        [SerializeField] private UpgradeDefinition Definition;

        [Tooltip("Current purchased level stored for debug inspection.")]
        [SerializeField] private int CurrentLevel = 0;

        /// <summary>
        /// Gets the definition referenced by this runtime entry.
        /// </summary>
        public UpgradeDefinition GetDefinition()
        {
            return Definition;
        }

        /// <summary>
        /// Gets the currently stored runtime level.
        /// </summary>
        public int GetCurrentLevel()
        {
            return CurrentLevel;
        }

        /// <summary>
        /// Sets the currently stored runtime level.
        /// </summary>
        public void SetCurrentLevel(int Level)
        {
            CurrentLevel = Mathf.Max(0, Level);
        }
    }

    [Header("References")]
    [Tooltip("Central wallet used to validate and spend upgrade costs.")]
    [SerializeField] private CurrencyWallet CurrencyWallet;

    [Header("Definitions")]
    [Tooltip("All upgrade definitions available in this runtime.")]
    [SerializeField] private List<UpgradeDefinition> UpgradeDefinitions = new();

    [Header("Runtime Debug")]
    [Tooltip("Inspector-visible mirror of runtime levels used only for debugging.")]
    [SerializeField] private List<UpgradeLevelEntry> DebugRuntimeLevels = new();

    [Tooltip("Logs upgrade validation and purchase operations.")]
    [SerializeField] private bool DebugLogs = false;

    private readonly Dictionary<string, UpgradeDefinition> DefinitionsById = new();
    private readonly Dictionary<string, int> LevelsById = new();
    private readonly HashSet<string> ActiveFeatureFlags = new();
    private readonly HashSet<string> ActiveVisualEffectIds = new();
    private readonly HashSet<ItemDefinition> ActiveUnlockedItems = new();

    /// <summary>
    /// Fired when the level of a specific upgrade changes.
    /// The first argument is the affected definition and the second is the new level.
    /// </summary>
    public event Action<UpgradeDefinition, int> OnUpgradeLevelChanged;

    /// <summary>
    /// Fired when the overall upgrade state changes and bound systems should refresh.
    /// </summary>
    public event Action OnUpgradeStateChanged;

    /// <summary>
    /// Initializes references and rebuilds definition and reward caches.
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
    /// Gets the full list of configured upgrade definitions.
    /// </summary>
    public IReadOnlyList<UpgradeDefinition> GetAllUpgradeDefinitions()
    {
        return UpgradeDefinitions;
    }

    /// <summary>
    /// Gets the purchased level of the provided upgrade definition.
    /// </summary>
    public int GetUpgradeLevel(UpgradeDefinition UpgradeDefinition)
    {
        if (UpgradeDefinition == null)
        {
            return 0;
        }

        return GetUpgradeLevelById(UpgradeDefinition.GetUpgradeId());
    }

    /// <summary>
    /// Gets the purchased level of the upgrade with the provided id.
    /// </summary>
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

    /// <summary>
    /// Gets the definition associated with the provided upgrade id.
    /// </summary>
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

    /// <summary>
    /// Returns whether all configured prerequisites are currently met for this upgrade.
    /// </summary>
    public bool AreUpgradePrerequisitesMet(UpgradeDefinition UpgradeDefinition)
    {
        return !TryGetFirstUnmetPrerequisite(UpgradeDefinition, out _);
    }

    /// <summary>
    /// Tries to get the first prerequisite that is currently not satisfied for the provided upgrade.
    /// </summary>
    public bool TryGetFirstUnmetPrerequisite(
        UpgradeDefinition UpgradeDefinition,
        out UpgradeDefinition.UpgradePrerequisiteDefinition UnmetPrerequisite
    )
    {
        UnmetPrerequisite = null;

        if (UpgradeDefinition == null)
        {
            return false;
        }

        IReadOnlyList<UpgradeDefinition.UpgradePrerequisiteDefinition> Prerequisites = UpgradeDefinition.GetPrerequisites();

        if (Prerequisites == null || Prerequisites.Count <= 0)
        {
            return false;
        }

        for (int Index = 0; Index < Prerequisites.Count; Index++)
        {
            UpgradeDefinition.UpgradePrerequisiteDefinition Prerequisite = Prerequisites[Index];

            if (Prerequisite == null)
            {
                continue;
            }

            UpgradeDefinition RequiredDefinition = Prerequisite.GetRequiredUpgradeDefinition();

            if (RequiredDefinition == null)
            {
                UnmetPrerequisite = Prerequisite;
                return true;
            }

            int CurrentRequiredLevel = GetUpgradeLevel(RequiredDefinition);

            if (CurrentRequiredLevel < Prerequisite.GetRequiredLevel())
            {
                UnmetPrerequisite = Prerequisite;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the current reason why this upgrade cannot be purchased.
    /// Returns None when the upgrade is purchasable right now.
    /// </summary>
    public UpgradePurchaseBlockReason GetPurchaseBlockReason(UpgradeDefinition UpgradeDefinition)
    {
        if (UpgradeDefinition == null)
        {
            return UpgradePurchaseBlockReason.MissingDefinition;
        }

        if (CurrencyWallet == null)
        {
            return UpgradePurchaseBlockReason.MissingCurrencyWallet;
        }

        int CurrentLevel = GetUpgradeLevel(UpgradeDefinition);

        if (CurrentLevel >= UpgradeDefinition.GetMaxLevel())
        {
            return UpgradePurchaseBlockReason.AlreadyMaxLevel;
        }

        int NextLevel = CurrentLevel + 1;
        UpgradeDefinition.UpgradeLevelCost NextCost = UpgradeDefinition.GetCostForLevel(NextLevel);

        if (NextCost == null)
        {
            return UpgradePurchaseBlockReason.MissingLevelCost;
        }

        if (TryGetFirstUnmetPrerequisite(UpgradeDefinition, out _))
        {
            return UpgradePurchaseBlockReason.MissingPrerequisite;
        }

        if (!CurrencyWallet.HasEnough(NextCost.GetCurrencyType(), NextCost.GetCost()))
        {
            return UpgradePurchaseBlockReason.NotEnoughCurrency;
        }

        return UpgradePurchaseBlockReason.None;
    }

    /// <summary>
    /// Returns whether the provided upgrade can currently be purchased.
    /// </summary>
    public bool CanPurchaseUpgrade(UpgradeDefinition UpgradeDefinition)
    {
        return GetPurchaseBlockReason(UpgradeDefinition) == UpgradePurchaseBlockReason.None;
    }

    /// <summary>
    /// Attempts to purchase the next level of the provided upgrade.
    /// </summary>
    public bool TryPurchaseUpgrade(UpgradeDefinition UpgradeDefinition)
    {
        UpgradePurchaseBlockReason BlockReason = GetPurchaseBlockReason(UpgradeDefinition);

        if (BlockReason != UpgradePurchaseBlockReason.None)
        {
            LogPurchaseBlocked(UpgradeDefinition, BlockReason);
            return false;
        }

        int CurrentLevel = GetUpgradeLevel(UpgradeDefinition);
        int NextLevel = CurrentLevel + 1;
        UpgradeDefinition.UpgradeLevelCost NextCost = UpgradeDefinition.GetCostForLevel(NextLevel);

        if (!CurrencyWallet.TrySpendCurrency(NextCost.GetCurrencyType(), NextCost.GetCost()))
        {
            Log("Purchase failed during spend attempt for upgrade " + UpgradeDefinition.GetDisplayName());
            return false;
        }

        SetUpgradeLevel(UpgradeDefinition, NextLevel);
        Log("Purchased upgrade " + UpgradeDefinition.GetDisplayName() + " to level " + NextLevel);
        return true;
    }

    /// <summary>
    /// Sets the runtime level of the provided upgrade definition.
    /// </summary>
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

    /// <summary>
    /// Gets the modified value for an ore-specific float stat.
    /// </summary>
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

    /// <summary>
    /// Gets the modified value for an ore-specific int stat.
    /// </summary>
    public int GetModifiedOreIntStat(UpgradeStatType StatType, string OreId, int BaseValue)
    {
        float ModifiedValue = GetModifiedOreFloatStat(StatType, OreId, BaseValue);
        return Mathf.RoundToInt(ModifiedValue);
    }

    /// <summary>
    /// Gets the modified value for a global float stat.
    /// </summary>
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

    /// <summary>
    /// Gets the modified value for a global int stat.
    /// </summary>
    public int GetModifiedIntStat(UpgradeStatType StatType, int BaseValue)
    {
        float ModifiedValue = GetModifiedFloatStat(StatType, BaseValue);
        return Mathf.RoundToInt(ModifiedValue);
    }

    /// <summary>
    /// Returns whether the provided feature flag reward is currently unlocked.
    /// </summary>
    public bool IsFeatureUnlocked(string FeatureFlagId)
    {
        if (string.IsNullOrWhiteSpace(FeatureFlagId))
        {
            return false;
        }

        return ActiveFeatureFlags.Contains(FeatureFlagId);
    }

    /// <summary>
    /// Returns whether the provided visual effect reward is currently unlocked.
    /// </summary>
    public bool IsVisualEffectUnlocked(string VisualEffectId)
    {
        if (string.IsNullOrWhiteSpace(VisualEffectId))
        {
            return false;
        }

        return ActiveVisualEffectIds.Contains(VisualEffectId);
    }

    /// <summary>
    /// Returns whether the provided item reward is currently unlocked.
    /// </summary>
    public bool IsItemUnlocked(ItemDefinition ItemDefinition)
    {
        if (ItemDefinition == null)
        {
            return false;
        }

        return ActiveUnlockedItems.Contains(ItemDefinition);
    }

    /// <summary>
    /// Rebuilds the definition lookup cache and initializes default runtime levels.
    /// </summary>
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

            LevelsById[UpgradeId] = Mathf.Clamp(
                Entry.GetCurrentLevel(),
                0,
                Entry.GetDefinition().GetMaxLevel()
            );
        }
    }

    /// <summary>
    /// Rebuilds the runtime caches used for unlocked non-stat rewards.
    /// </summary>
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

    /// <summary>
    /// Keeps the inspector debug list synchronized with runtime level changes.
    /// </summary>
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

    /// <summary>
    /// Applies the provided modifier operation to the current float value.
    /// </summary>
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

    /// <summary>
    /// Logs a purchase rejection with the most relevant context for the current block reason.
    /// </summary>
    private void LogPurchaseBlocked(UpgradeDefinition UpgradeDefinition, UpgradePurchaseBlockReason BlockReason)
    {
        if (UpgradeDefinition == null)
        {
            Log("Purchase blocked because the provided upgrade definition is null.");
            return;
        }

        switch (BlockReason)
        {
            case UpgradePurchaseBlockReason.MissingCurrencyWallet:
                Log("Purchase blocked for " + UpgradeDefinition.GetDisplayName() + " because CurrencyWallet is missing.");
                break;

            case UpgradePurchaseBlockReason.AlreadyMaxLevel:
                Log("Upgrade is already at max level: " + UpgradeDefinition.GetDisplayName());
                break;

            case UpgradePurchaseBlockReason.MissingLevelCost:
                Log("Missing configured cost for next level on upgrade " + UpgradeDefinition.GetDisplayName());
                break;

            case UpgradePurchaseBlockReason.MissingPrerequisite:
                if (TryGetFirstUnmetPrerequisite(UpgradeDefinition, out UpgradeDefinition.UpgradePrerequisiteDefinition UnmetPrerequisite) &&
                    UnmetPrerequisite != null)
                {
                    UpgradeDefinition RequiredDefinition = UnmetPrerequisite.GetRequiredUpgradeDefinition();
                    string RequiredName = RequiredDefinition != null ? RequiredDefinition.GetDisplayName() : "Missing Upgrade Reference";

                    Log(
                        "Purchase blocked for " + UpgradeDefinition.GetDisplayName() +
                        " because prerequisite is not met: " +
                        RequiredName +
                        " level " + UnmetPrerequisite.GetRequiredLevel()
                    );
                }
                else
                {
                    Log("Purchase blocked for " + UpgradeDefinition.GetDisplayName() + " because prerequisites are not met.");
                }
                break;

            case UpgradePurchaseBlockReason.NotEnoughCurrency:
                Log("Not enough currency to purchase upgrade " + UpgradeDefinition.GetDisplayName());
                break;

            default:
                Log("Purchase blocked for " + UpgradeDefinition.GetDisplayName() + " due to " + BlockReason);
                break;
        }
    }

    /// <summary>
    /// Logs upgrade manager messages if debug logging is enabled.
    /// </summary>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[UpgradeManager] " + Message, this);
    }
}