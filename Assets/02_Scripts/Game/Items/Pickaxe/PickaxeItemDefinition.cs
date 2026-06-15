using UnityEngine;

/// <summary>
/// Specialized item definition for pickaxes.
/// It keeps pickaxe mining stats in data assets instead of hardcoding them inside the equipped behaviour.
/// Existing ItemDefinition pickaxes still work through the fallback values on PickaxeItemBehaviour.
/// </summary>
[CreateAssetMenu(fileName = "PickaxeDefinition_", menuName = "Game/Items/Pickaxe Definition")]
public sealed class PickaxeItemDefinition : ItemDefinition
{
    [Header("Pickaxe Mining")]
    [Tooltip("Mining damage applied to ore durability on each accepted impact.")]
    [SerializeField] private float MiningDamage = 1f;

    [Tooltip("Mining tier provided by this pickaxe. Ores that require a higher tier reject the hit without consuming durability.")]
    [SerializeField] private MiningTier MiningTier = MiningTier.TierI;

    [Tooltip("Multiplier applied to generated ore purity when this pickaxe breaks a vein. Lower values represent rough extraction.")]
    [SerializeField] private float ExtractionQualityMultiplier = 1f;

    [Header("Pickaxe Durability")]
    [Tooltip("If true, accepted mining hits consume durability from the runtime item instance.")]
    [SerializeField] private bool UsesDurability = true;

    [Tooltip("Durability consumed after each accepted mining hit.")]
    [SerializeField] private float DurabilityCostPerAcceptedHit = 1f;

    [Tooltip("If true, the pickaxe is removed from the hotbar when durability reaches zero.")]
    [SerializeField] private bool BreaksAtZeroDurability = true;

    [Header("Pickaxe Timing")]
    [Tooltip("Minimum seconds between accepted primary action starts. This is a safety gate in addition to animation-event timing.")]
    [SerializeField] private float MinimumUseInterval = 0f;

    [Header("Pickaxe Feedback")]
    [Tooltip("Generic feedback profile used by the equipped pickaxe when mining events happen.")]
    [SerializeField] private GameFeedbackProfile FeedbackProfile;

    /// <summary>
    /// Gets the mining damage applied by this pickaxe.
    /// </summary>
    public float GetMiningDamage()
    {
        return Mathf.Max(0f, MiningDamage);
    }

    /// <summary>
    /// Gets the mining tier provided by this pickaxe.
    /// </summary>
    public MiningTier GetMiningTier()
    {
        return MiningTier == MiningTier.None ? MiningTier.TierI : MiningTier;
    }

    /// <summary>
    /// Gets the extraction quality multiplier applied to ore purity when this pickaxe breaks a vein.
    /// </summary>
    public float GetExtractionQualityMultiplier()
    {
        return Mathf.Max(0.01f, ExtractionQualityMultiplier);
    }

    /// <summary>
    /// Gets whether this pickaxe consumes durability on accepted mining hits.
    /// </summary>
    public bool GetUsesDurability()
    {
        return UsesDurability;
    }

    /// <summary>
    /// Gets the durability amount consumed after each accepted mining hit.
    /// </summary>
    public float GetDurabilityCostPerAcceptedHit()
    {
        return Mathf.Max(0f, DurabilityCostPerAcceptedHit);
    }

    /// <summary>
    /// Gets whether this pickaxe should be removed when durability reaches zero.
    /// </summary>
    public bool GetBreaksAtZeroDurability()
    {
        return BreaksAtZeroDurability;
    }

    /// <summary>
    /// Gets the minimum time between primary action starts.
    /// </summary>
    public float GetMinimumUseInterval()
    {
        return Mathf.Max(0f, MinimumUseInterval);
    }

    /// <summary>
    /// Gets the generic feedback profile used by this pickaxe.
    /// </summary>
    /// <returns>Feedback profile assigned to this pickaxe definition, or null.</returns>
    public GameFeedbackProfile GetFeedbackProfile()
    {
        return FeedbackProfile;
    }
}
