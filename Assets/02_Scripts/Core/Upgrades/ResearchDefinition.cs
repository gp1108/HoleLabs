using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines one research entry performed through the researcher station.
/// The research is activated first by spending credits, then completed progressively by processing matching physical ores.
/// </summary>
[CreateAssetMenu(fileName = "Research_", menuName = "Game/Research/Research Definition")]
public sealed class ResearchDefinition : ScriptableObject
{
    /// <summary>
    /// Defines how the referenced upgrade level is modified when this research completes.
    /// </summary>
    public enum ResearchApplyMode
    {
        SetToLevel = 0,
        AddLevels = 1
    }

    /// <summary>
    /// Defines one required ore filter and amount for this research.
    /// Filters can be combined, for example coal with minimum purity and minimum weight.
    /// </summary>
    [Serializable]
    public sealed class OreRequirement
    {
        [Tooltip("Ore definition required by this research.")]
        [SerializeField] private OreDefinition OreDefinition;

        [Tooltip("Amount of matching physical ore pickups required by this research step.")]
        [SerializeField] private int Amount = 1;

        [Header("Purity Filter")]
        [Tooltip("If true, only ores with purity equal or above Minimum Purity are accepted.")]
        [SerializeField] private bool RequireMinimumPurity = false;

        [Tooltip("Minimum accepted ore purity percent. Use 20 for 20% and 100 for perfect purity.")]
        [SerializeField, Range(0f, 100f)] private float MinimumPurity = 20f;

        [Tooltip("If true, only ores with purity equal or below Maximum Purity are accepted.")]
        [SerializeField] private bool RequireMaximumPurity = false;

        [Tooltip("Maximum accepted ore purity percent. Use 80 for 80% and 100 for perfect purity.")]
        [SerializeField, Range(0f, 100f)] private float MaximumPurity = 100f;

        [Header("Weight Filter")]
        [Tooltip("If true, only ores with weight equal or above Minimum Weight are accepted.")]
        [SerializeField] private bool RequireMinimumWeight = false;

        [Tooltip("Minimum accepted ore weight.")]
        [SerializeField] private float MinimumWeight = 1f;

        [Tooltip("If true, only ores with weight equal or below Maximum Weight are accepted.")]
        [SerializeField] private bool RequireMaximumWeight = false;

        [Tooltip("Maximum accepted ore weight.")]
        [SerializeField] private float MaximumWeight = 999f;

        /// <summary>
        /// Gets the required ore definition.
        /// </summary>
        public OreDefinition GetOreDefinition()
        {
            return OreDefinition;
        }

        /// <summary>
        /// Gets the required amount of matching ores.
        /// </summary>
        public int GetAmount()
        {
            return Mathf.Max(0, Amount);
        }

        /// <summary>
        /// Returns whether this requirement has a valid ore and positive amount.
        /// </summary>
        public bool IsValid()
        {
            return OreDefinition != null && GetAmount() > 0;
        }

        /// <summary>
        /// Gets a stable display name for this requirement.
        /// </summary>
        public string GetDisplayName()
        {
            return OreDefinition != null ? OreDefinition.GetDisplayName() : "Missing Ore";
        }

        /// <summary>
        /// Returns whether the provided pickup satisfies this ore requirement and every enabled filter.
        /// </summary>
        /// <param name="Pickup">Ore pickup being evaluated.</param>
        public bool MatchesPickup(OrePickup Pickup)
        {
            if (Pickup == null || OreDefinition == null)
            {
                return false;
            }

            OreItemData ItemData = Pickup.GetOreItemData();

            if (ItemData == null || ItemData.GetOreDefinition() == null)
            {
                return false;
            }

            OreDefinition PickupDefinition = ItemData.GetOreDefinition();
            bool IsSameOre = PickupDefinition == OreDefinition ||
                             string.Equals(PickupDefinition.GetOreId(), OreDefinition.GetOreId(), StringComparison.Ordinal);

            if (!IsSameOre)
            {
                return false;
            }

            float Purity = ItemData.GetPurityPercent();
            float Weight = ItemData.GetWeightValue();

            if (RequireMinimumPurity && Purity < MinimumPurity)
            {
                return false;
            }

            if (RequireMaximumPurity && Purity > MaximumPurity)
            {
                return false;
            }

            if (RequireMinimumWeight && Weight < MinimumWeight)
            {
                return false;
            }

            if (RequireMaximumWeight && Weight > MaximumWeight)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Builds a readable requirement label including enabled filters.
        /// </summary>
        public string BuildDisplayRequirementLabel()
        {
            string Label = GetDisplayName() + " x" + GetAmount();

            if (RequireMinimumPurity)
            {
                Label += " | Purity >= " + MinimumPurity.ToString("0.#") + "%";
            }

            if (RequireMaximumPurity)
            {
                Label += " | Purity <= " + MaximumPurity.ToString("0.#") + "%";
            }

            if (RequireMinimumWeight)
            {
                Label += " | Weight >= " + MinimumWeight.ToString("0.##");
            }

            if (RequireMaximumWeight)
            {
                Label += " | Weight <= " + MaximumWeight.ToString("0.##");
            }

            return Label;
        }
    }

    /// <summary>
    /// Defines one prerequisite upgrade level that must already be owned before this research is available.
    /// </summary>
    [Serializable]
    public sealed class ResearchPrerequisite
    {
        [Tooltip("Upgrade that must already be owned before this research can be activated.")]
        [SerializeField] private UpgradeDefinition RequiredUpgradeDefinition;

        [Tooltip("Minimum level required on the referenced upgrade.")]
        [SerializeField] private int RequiredLevel = 1;

        /// <summary>
        /// Gets the required upgrade definition.
        /// </summary>
        public UpgradeDefinition GetRequiredUpgradeDefinition()
        {
            return RequiredUpgradeDefinition;
        }

        /// <summary>
        /// Gets the minimum required level.
        /// </summary>
        public int GetRequiredLevel()
        {
            return Mathf.Max(1, RequiredLevel);
        }
    }

    [Header("Identity")]
    [Tooltip("Unique research id used for runtime progress and future save migrations.")]
    [SerializeField] private string ResearchId;

    [Tooltip("Display name shown in the research UI.")]
    [SerializeField] private string DisplayName;

    [Tooltip("Description shown in the research details panel or entry.")]
    [TextArea]
    [SerializeField] private string Description;

    [Tooltip("Optional icon shown in UI.")]
    [SerializeField] private Sprite Icon;

    [Header("Activation Cost")]
    [Tooltip("Credit amount spent when this research becomes the active research for the first time.")]
    [SerializeField] private float CreditCost = 100f;

    [Header("Ore Processing Requirements")]
    [Tooltip("Specific physical ore requirements processed while this research is active.")]
    [SerializeField] private List<OreRequirement> OreRequirements = new();

    [Header("Availability")]
    [Tooltip("If true, this research only becomes available after a feature flag is unlocked.")]
    [SerializeField] private bool RequiresFeatureFlag = false;

    [Tooltip("Feature flag required when Requires Feature Flag is enabled.")]
    [SerializeField] private string RequiredFeatureFlagId;

    [Tooltip("Upgrade prerequisites required before this research can be activated.")]
    [SerializeField] private List<ResearchPrerequisite> Prerequisites = new();

    [Header("Result")]
    [Tooltip("Upgrade modified when this research completes. The upgrade must be registered in UpgradeManager.")]
    [SerializeField] private UpgradeDefinition AppliedUpgradeDefinition;

    [Tooltip("How the referenced upgrade level is modified when this research completes.")]
    [SerializeField] private ResearchApplyMode ApplyMode = ResearchApplyMode.SetToLevel;

    [Tooltip("Target level used by Set To Level mode.")]
    [SerializeField] private int TargetUpgradeLevel = 1;

    [Tooltip("Level increment used by Add Levels mode.")]
    [SerializeField] private int UpgradeLevelIncrement = 1;

    /// <summary>
    /// Gets the unique research id.
    /// </summary>
    public string GetResearchId()
    {
        return ResearchId;
    }

    /// <summary>
    /// Gets the display name shown in UI.
    /// </summary>
    public string GetDisplayName()
    {
        return DisplayName;
    }

    /// <summary>
    /// Gets the description shown in UI.
    /// </summary>
    public string GetDescription()
    {
        return Description;
    }

    /// <summary>
    /// Gets the optional icon shown in UI.
    /// </summary>
    public Sprite GetIcon()
    {
        return Icon;
    }

    /// <summary>
    /// Gets the rounded credit cost paid when the research is first activated.
    /// </summary>
    public float GetCreditCost()
    {
        return CurrencyMath.RoundCurrency(Mathf.Max(0f, CreditCost));
    }

    /// <summary>
    /// Gets the configured ore requirements.
    /// </summary>
    public IReadOnlyList<OreRequirement> GetOreRequirements()
    {
        return OreRequirements;
    }

    /// <summary>
    /// Gets whether this research requires a feature flag before activation.
    /// </summary>
    public bool GetRequiresFeatureFlag()
    {
        return RequiresFeatureFlag;
    }

    /// <summary>
    /// Gets the required feature flag id.
    /// </summary>
    public string GetRequiredFeatureFlagId()
    {
        return RequiredFeatureFlagId;
    }

    /// <summary>
    /// Gets the configured prerequisite upgrades.
    /// </summary>
    public IReadOnlyList<ResearchPrerequisite> GetPrerequisites()
    {
        return Prerequisites;
    }

    /// <summary>
    /// Gets the upgrade applied by this research.
    /// </summary>
    public UpgradeDefinition GetAppliedUpgradeDefinition()
    {
        return AppliedUpgradeDefinition;
    }

    /// <summary>
    /// Gets the configured apply mode.
    /// </summary>
    public ResearchApplyMode GetApplyMode()
    {
        return ApplyMode;
    }

    /// <summary>
    /// Gets the target level used by Set To Level mode.
    /// </summary>
    public int GetTargetUpgradeLevel()
    {
        return Mathf.Max(0, TargetUpgradeLevel);
    }

    /// <summary>
    /// Gets the level increment used by Add Levels mode.
    /// </summary>
    public int GetUpgradeLevelIncrement()
    {
        return Mathf.Max(1, UpgradeLevelIncrement);
    }

    /// <summary>
    /// Gets the next target level that would be applied from the current upgrade manager state.
    /// </summary>
    /// <param name="UpgradeManager">Upgrade manager used to read the current level.</param>
    public int GetResolvedTargetLevel(UpgradeManager UpgradeManager)
    {
        if (AppliedUpgradeDefinition == null)
        {
            return 0;
        }

        int CurrentLevel = UpgradeManager != null ? UpgradeManager.GetUpgradeLevel(AppliedUpgradeDefinition) : 0;

        switch (ApplyMode)
        {
            case ResearchApplyMode.SetToLevel:
                return Mathf.Clamp(GetTargetUpgradeLevel(), 0, AppliedUpgradeDefinition.GetMaxLevel());

            case ResearchApplyMode.AddLevels:
                return Mathf.Clamp(CurrentLevel + GetUpgradeLevelIncrement(), 0, AppliedUpgradeDefinition.GetMaxLevel());

            default:
                return CurrentLevel;
        }
    }
}
