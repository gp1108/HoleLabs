using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Static definition for one mineable ore type.
/// This asset owns identity, mining requirements, dropped ore prefabs and explicit runtime quality ranges.
/// Purity is configured as a readable percent from 0 to 100, while size uses natural scale units where 1 is normal size.
/// </summary>
[CreateAssetMenu(fileName = "OreDefinition_", menuName = "Game/Mining/Ore Definition")]
public sealed class OreDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable ore identifier used by saves, upgrades and progression systems.")]
    [SerializeField] private string OreId;

    [Tooltip("Display name shown in scanner UI and debug tools.")]
    [SerializeField] private string DisplayName;

    [Tooltip("Optional ore icon used by UI.")]
    [SerializeField] private Sprite Icon;

    [Header("World")]
    [Tooltip("Fallback dropped ore prefab used when the visual variants list is empty.")]
    [SerializeField] private GameObject DroppedOrePrefab;

    [Tooltip("Optional list of dropped ore visual prefabs. A random one is selected every time a drop is spawned.")]
    [SerializeField] private List<GameObject> DroppedOreVisualPrefabs = new();

    [Header("Mining")]
    [Tooltip("Minimum mining tier required to damage this ore. Lower tier tools will be rejected without consuming durability.")]
    [SerializeField] private MiningTier RequiredMiningTier = MiningTier.TierI;

    [Tooltip("Base amount of mining damage required to break this ore vein.")]
    [FormerlySerializedAs("BaseHitsRequired")]
    [SerializeField] private int BaseMiningDurability = 3;

    [Tooltip("Seconds required before this ore vein regrows after being depleted.")]
    [SerializeField] private float BaseRespawnTime = 30f;

    [Tooltip("Minimum amount of physical ore pickups spawned when this vein breaks.")]
    [SerializeField] private int BaseDropCountMin = 1;

    [Tooltip("Maximum amount of physical ore pickups spawned when this vein breaks.")]
    [SerializeField] private int BaseDropCountMax = 2;

    [Header("Runtime Purity")]
    [Tooltip("Minimum generated purity percent for each dropped ore. 0 means completely impure and 100 means perfectly pure.")]
    [SerializeField, Range(0f, 100f)] private float MinPurityPercent = 35f;

    [Tooltip("Maximum generated purity percent for each dropped ore. 0 means completely impure and 100 means perfectly pure.")]
    [SerializeField, Range(0f, 100f)] private float MaxPurityPercent = 85f;

    [Header("Runtime Size")]
    [Tooltip("Minimum generated size scale for each dropped ore. 1 is normal size, 0.5 is half size and 2 is double size.")]
    [SerializeField] private float MinSizeScale = 0.85f;

    [Tooltip("Maximum generated size scale for each dropped ore. 1 is normal size, 0.5 is half size and 2 is double size.")]
    [SerializeField] private float MaxSizeScale = 1.25f;

    [Header("Credit Value")]
    [Tooltip("Minimum final credit value for one dropped ore before global upgrade multipliers and flat bonuses.")]
    [FormerlySerializedAs("BaseCreditValueMin")]
    [SerializeField] private float MinCreditValue = 3f;

    [Tooltip("Maximum final credit value for one dropped ore before global upgrade multipliers and flat bonuses.")]
    [FormerlySerializedAs("BaseCreditValueMax")]
    [SerializeField] private float MaxCreditValue = 6f;

    [Tooltip("How much purity contributes when interpolating between minimum and maximum credit value. Use 1 for full influence, 0 to ignore purity.")]
    [SerializeField, Min(0f)] private float PurityCreditContribution = 0.5f;

    [Tooltip("How much size contributes when interpolating between minimum and maximum credit value. Use 1 for full influence, 0 to ignore size.")]
    [SerializeField, Min(0f)] private float SizeCreditContribution = 0.5f;

    [Header("Weight")]
    [Tooltip("Minimum final physical weight for one dropped ore.")]
    [FormerlySerializedAs("BaseWeightValue")]
    [SerializeField] private float MinWeightValue = 1f;

    [Tooltip("Maximum final physical weight for one dropped ore.")]
    [SerializeField] private float MaxWeightValue = 3f;

    [Tooltip("How much purity contributes when interpolating between minimum and maximum weight. Use 1 for full influence, 0 to ignore purity.")]
    [SerializeField, Min(0f)] private float PurityWeightContribution = 0.5f;

    [Tooltip("How much size contributes when interpolating between minimum and maximum weight. Use 1 for full influence, 0 to ignore size.")]
    [SerializeField, Min(0f)] private float SizeWeightContribution = 0.5f;

    /// <summary>
    /// Gets the stable ore identifier.
    /// </summary>
    public string GetOreId() => OreId;

    /// <summary>
    /// Gets the display name shown to the player.
    /// </summary>
    public string GetDisplayName() => DisplayName;

    /// <summary>
    /// Gets the optional ore icon.
    /// </summary>
    public Sprite GetIcon() => Icon;

    /// <summary>
    /// Gets the dropped ore prefab fallback.
    /// </summary>
    public GameObject GetDroppedOrePrefab() => DroppedOrePrefab;

    /// <summary>
    /// Gets the optional dropped ore visual prefab variants.
    /// </summary>
    public IReadOnlyList<GameObject> GetDroppedOreVisualPrefabs() => DroppedOreVisualPrefabs;

    /// <summary>
    /// Gets the minimum mining tier required to damage this ore.
    /// </summary>
    public MiningTier GetRequiredMiningTier() => RequiredMiningTier == MiningTier.None ? MiningTier.TierI : RequiredMiningTier;

    /// <summary>
    /// Gets the base mining durability required before this ore breaks.
    /// </summary>
    public int GetBaseMiningDurability() => Mathf.Max(1, BaseMiningDurability);

    /// <summary>
    /// Gets the base respawn time in seconds.
    /// </summary>
    public float GetBaseRespawnTime() => Mathf.Max(0f, BaseRespawnTime);

    /// <summary>
    /// Gets the minimum amount of drops created when this ore breaks.
    /// </summary>
    public int GetBaseDropCountMin() => Mathf.Max(0, BaseDropCountMin);

    /// <summary>
    /// Gets the maximum amount of drops created when this ore breaks.
    /// </summary>
    public int GetBaseDropCountMax() => Mathf.Max(GetBaseDropCountMin(), BaseDropCountMax);

    /// <summary>
    /// Gets the minimum generated purity percent.
    /// </summary>
    public float GetMinPurityPercent() => Mathf.Clamp(Mathf.Min(MinPurityPercent, MaxPurityPercent), 0f, 100f);

    /// <summary>
    /// Gets the maximum generated purity percent.
    /// </summary>
    public float GetMaxPurityPercent() => Mathf.Clamp(Mathf.Max(MinPurityPercent, MaxPurityPercent), 0f, 100f);

    /// <summary>
    /// Gets the minimum generated size scale.
    /// </summary>
    public float GetMinSizeScale() => Mathf.Max(0.01f, Mathf.Min(MinSizeScale, MaxSizeScale));

    /// <summary>
    /// Gets the maximum generated size scale.
    /// </summary>
    public float GetMaxSizeScale() => Mathf.Max(GetMinSizeScale(), Mathf.Max(MinSizeScale, MaxSizeScale));

    /// <summary>
    /// Converts a natural size scale into a normalized 0..1 value inside this ore definition's configured size range.
    /// </summary>
    /// <param name="SizeScale">Natural size scale to normalize.</param>
    /// <returns>Normalized size value.</returns>
    public float NormalizeSizeScale(float SizeScale)
    {
        float MinimumSize = GetMinSizeScale();
        float MaximumSize = GetMaxSizeScale();

        if (Mathf.Approximately(MinimumSize, MaximumSize))
        {
            return 1f;
        }

        return Mathf.Clamp01(Mathf.InverseLerp(MinimumSize, MaximumSize, Mathf.Max(0.01f, SizeScale)));
    }

    /// <summary>
    /// Gets the minimum credit value for one dropped ore before upgrades.
    /// </summary>
    public float GetMinCreditValue() => CurrencyMath.RoundCurrency(Mathf.Max(0f, MinCreditValue));

    /// <summary>
    /// Gets the maximum credit value for one dropped ore before upgrades.
    /// </summary>
    public float GetMaxCreditValue() => CurrencyMath.RoundCurrency(Mathf.Max(GetMinCreditValue(), MaxCreditValue));

    /// <summary>
    /// Legacy alias for old systems. Use GetMinCreditValue instead.
    /// </summary>
    public float GetBaseCreditValueMin() => GetMinCreditValue();

    /// <summary>
    /// Legacy alias for old systems. Use GetMaxCreditValue instead.
    /// </summary>
    public float GetBaseCreditValueMax() => GetMaxCreditValue();

    /// <summary>
    /// Gets how much purity contributes to credit value interpolation.
    /// </summary>
    public float GetPurityCreditContribution() => Mathf.Max(0f, PurityCreditContribution);

    /// <summary>
    /// Gets how much size contributes to credit value interpolation.
    /// </summary>
    public float GetSizeCreditContribution() => Mathf.Max(0f, SizeCreditContribution);

    /// <summary>
    /// Gets the minimum physical weight for one dropped ore.
    /// </summary>
    public float GetMinWeightValue() => Mathf.Max(0f, MinWeightValue);

    /// <summary>
    /// Gets the maximum physical weight for one dropped ore.
    /// </summary>
    public float GetMaxWeightValue() => Mathf.Max(GetMinWeightValue(), MaxWeightValue);

    /// <summary>
    /// Legacy alias for old systems. Use GetMinWeightValue and GetMaxWeightValue instead.
    /// </summary>
    public float GetBaseWeightValue() => (GetMinWeightValue() + GetMaxWeightValue()) * 0.5f;

    /// <summary>
    /// Gets how much purity contributes to weight interpolation.
    /// </summary>
    public float GetPurityWeightContribution() => Mathf.Max(0f, PurityWeightContribution);

    /// <summary>
    /// Gets how much size contributes to weight interpolation.
    /// </summary>
    public float GetSizeWeightContribution() => Mathf.Max(0f, SizeWeightContribution);

    /// <summary>
    /// Returns a random valid dropped ore prefab.
    /// Uses the visual variants list when available and falls back to the single fallback prefab otherwise.
    /// </summary>
    /// <returns>Dropped ore prefab or null when none is configured.</returns>
    public GameObject GetRandomDroppedOrePrefab()
    {
        if (DroppedOreVisualPrefabs != null && DroppedOreVisualPrefabs.Count > 0)
        {
            List<GameObject> ValidPrefabs = null;

            for (int Index = 0; Index < DroppedOreVisualPrefabs.Count; Index++)
            {
                if (DroppedOreVisualPrefabs[Index] == null)
                {
                    continue;
                }

                if (ValidPrefabs == null)
                {
                    ValidPrefabs = new List<GameObject>();
                }

                ValidPrefabs.Add(DroppedOreVisualPrefabs[Index]);
            }

            if (ValidPrefabs != null && ValidPrefabs.Count > 0)
            {
                int RandomIndex = Random.Range(0, ValidPrefabs.Count);
                return ValidPrefabs[RandomIndex];
            }
        }

        return DroppedOrePrefab;
    }

    /// <summary>
    /// Keeps inspector values coherent and reports configurations that cannot affect variable values.
    /// </summary>
    private void OnValidate()
    {
        BaseMiningDurability = Mathf.Max(1, BaseMiningDurability);
        BaseRespawnTime = Mathf.Max(0f, BaseRespawnTime);
        BaseDropCountMin = Mathf.Max(0, BaseDropCountMin);
        BaseDropCountMax = Mathf.Max(BaseDropCountMin, BaseDropCountMax);
        MinPurityPercent = Mathf.Clamp(MinPurityPercent, 0f, 100f);
        MaxPurityPercent = Mathf.Clamp(MaxPurityPercent, 0f, 100f);
        MinSizeScale = Mathf.Max(0.01f, MinSizeScale);
        MaxSizeScale = Mathf.Max(MinSizeScale, MaxSizeScale);
        MinCreditValue = CurrencyMath.RoundCurrency(Mathf.Max(0f, MinCreditValue));
        MaxCreditValue = CurrencyMath.RoundCurrency(Mathf.Max(MinCreditValue, MaxCreditValue));
        MinWeightValue = Mathf.Max(0f, MinWeightValue);
        MaxWeightValue = Mathf.Max(MinWeightValue, MaxWeightValue);
        PurityCreditContribution = Mathf.Max(0f, PurityCreditContribution);
        SizeCreditContribution = Mathf.Max(0f, SizeCreditContribution);
        PurityWeightContribution = Mathf.Max(0f, PurityWeightContribution);
        SizeWeightContribution = Mathf.Max(0f, SizeWeightContribution);
    }
}
