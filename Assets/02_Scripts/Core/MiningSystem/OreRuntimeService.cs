using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Central mining resolver used to translate ore definitions into runtime results.
/// This service acts as the single place where upgrades affect mining durability,
/// respawn time, drop count, ore properties, ore credit value and ore weight.
/// </summary>
public sealed class OreRuntimeService : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Runtime upgrade manager used to resolve purchased modifiers.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional ore pickup pool used when spawning dropped physical ores.")]
    [SerializeField] private OrePickupPool OrePickupPool;

    [Header("Credit Value Influence")]
    [Tooltip("How strongly runtime purity affects final credit value.")]
    [FormerlySerializedAs("PurityGoldInfluence")]
    [SerializeField] private float PurityCreditInfluence = 0.35f;

    [Tooltip("How strongly runtime size affects final credit value.")]
    [FormerlySerializedAs("SizeGoldInfluence")]
    [SerializeField] private float SizeCreditInfluence = 0.25f;


    [Header("Weight Influence")]
    [Tooltip("How strongly runtime size affects final physical ore weight.")]
    [SerializeField] private float SizeWeightInfluence = 0.75f;

    [Tooltip("How strongly runtime purity affects final physical ore weight.")]
    [SerializeField] private float PurityWeightInfluence = 0.10f;

    [Header("Debug")]
    [Tooltip("Logs ore runtime value resolution.")]
    [SerializeField] private bool DebugLogs = false;

    /// <summary>
    /// Resolves the amount of mining durability required by this ore after upgrades.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to resolve.</param>
    /// <returns>Final required mining durability.</returns>
    public int ResolveMiningDurability(OreDefinition OreDefinition)
    {
        if (OreDefinition == null)
        {
            return 1;
        }

        int BaseMiningDurability = OreDefinition.GetBaseMiningDurability();

        if (UpgradeManager == null)
        {
            return Mathf.Max(1, BaseMiningDurability);
        }

        int FinalMiningDurability = UpgradeManager.GetModifiedOreIntStat(
            UpgradeStatType.MiningHitsRequired,
            OreDefinition.GetOreId(),
            BaseMiningDurability);

        return Mathf.Max(1, FinalMiningDurability);
    }


    /// <summary>
    /// Resolves the ore respawn time after global upgrades.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to resolve.</param>
    /// <returns>Final respawn time in seconds.</returns>
    public float ResolveRespawnTime(OreDefinition OreDefinition)
    {
        if (OreDefinition == null)
        {
            return 0f;
        }

        float BaseRespawnTime = OreDefinition.GetBaseRespawnTime();

        if (UpgradeManager == null)
        {
            return Mathf.Max(0f, BaseRespawnTime);
        }

        float RespawnMultiplier = UpgradeManager.GetModifiedFloatStat(
            UpgradeStatType.OreRespawnTimeMultiplier,
            1f);

        return Mathf.Max(0f, BaseRespawnTime * Mathf.Max(0.01f, RespawnMultiplier));
    }

    /// <summary>
    /// Resolves the amount of dropped ore pickups created when this ore breaks.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to resolve.</param>
    /// <returns>Final random drop count.</returns>
    public int ResolveDropCount(OreDefinition OreDefinition)
    {
        if (OreDefinition == null)
        {
            return 0;
        }

        int FinalDropCountMin = OreDefinition.GetBaseDropCountMin();
        int FinalDropCountMax = OreDefinition.GetBaseDropCountMax();

        if (UpgradeManager != null)
        {
            FinalDropCountMin = UpgradeManager.GetModifiedOreIntStat(
                UpgradeStatType.OreYieldAmountMin,
                OreDefinition.GetOreId(),
                FinalDropCountMin);

            FinalDropCountMax = UpgradeManager.GetModifiedOreIntStat(
                UpgradeStatType.OreYieldAmountMax,
                OreDefinition.GetOreId(),
                FinalDropCountMax);
        }

        FinalDropCountMin = Mathf.Max(0, FinalDropCountMin);
        FinalDropCountMax = Mathf.Max(FinalDropCountMin, FinalDropCountMax);

        return Random.Range(FinalDropCountMin, FinalDropCountMax + 1);
    }

    /// <summary>
    /// Creates a runtime ore payload from a static ore definition using neutral extraction quality.
    /// </summary>
    /// <param name="OreDefinition">Ore definition used to create the runtime payload.</param>
    /// <returns>Runtime ore payload or null.</returns>
    public OreItemData CreateOreItemData(OreDefinition OreDefinition)
    {
        return CreateOreItemData(OreDefinition, 1f);
    }

    /// <summary>
    /// Creates a runtime ore payload from a static ore definition and applies the provided extraction quality to purity.
    /// </summary>
    /// <param name="OreDefinition">Ore definition used to create the runtime payload.</param>
    /// <param name="ExtractionQualityMultiplier">Quality multiplier applied to generated purity values.</param>
    /// <returns>Runtime ore payload or null.</returns>
    public OreItemData CreateOreItemData(OreDefinition OreDefinition, float ExtractionQualityMultiplier)
    {
        if (OreDefinition == null)
        {
            return null;
        }

        float SafeExtractionQualityMultiplier = Mathf.Max(0.01f, ExtractionQualityMultiplier);
        OreItemData OreItemData = new OreItemData(OreDefinition);
        var PropertyRanges = OreDefinition.GetPropertyRanges();

        for (int Index = 0; Index < PropertyRanges.Count; Index++)
        {
            OreDefinition.OrePropertyRange PropertyRange = PropertyRanges[Index];

            if (PropertyRange == null || PropertyRange.GetPropertyType() == OrePropertyType.None)
            {
                continue;
            }

            float RandomValue = Random.Range(
                PropertyRange.GetMinValue(),
                PropertyRange.GetMaxValue());

            if (PropertyRange.GetAffectedByUpgrades())
            {
                RandomValue = ApplyPropertyUpgradeMultiplier(
                    PropertyRange.GetPropertyType(),
                    RandomValue);
            }

            if (PropertyRange.GetPropertyType() == OrePropertyType.Purity)
            {
                RandomValue *= SafeExtractionQualityMultiplier;
            }

            OreItemData.SetProperty(PropertyRange.GetPropertyType(), RandomValue);
        }

        ResolveOreValues(OreItemData);

        Log("Created ore data for " + OreDefinition.GetDisplayName() + " with extraction quality " + SafeExtractionQualityMultiplier.ToString("0.00"));
        return OreItemData;
    }

    /// <summary>
    /// Resolves final credit value and physical weight for a runtime ore payload.
    /// </summary>
    /// <param name="OreItemData">Runtime ore payload to resolve.</param>
    public void ResolveOreValues(OreItemData OreItemData)
    {
        if (OreItemData == null || OreItemData.GetOreDefinition() == null)
        {
            return;
        }

        OreDefinition OreDefinition = OreItemData.GetOreDefinition();
        string OreId = OreDefinition.GetOreId();

        float Purity = Mathf.Max(0.01f, OreItemData.GetPropertyValue(OrePropertyType.Purity, 1f));
        float Size = Mathf.Max(0.01f, OreItemData.GetPropertyValue(OrePropertyType.Size, 1f));

        float BaseCreditRoll = CurrencyMath.RoundCurrency(Random.Range(
            OreDefinition.GetBaseCreditValueMin(),
            OreDefinition.GetBaseCreditValueMax()));

        float GlobalCreditMultiplier = 1f;
        float PerOreCreditMultiplier = 1f;
        float PerOreFlatCreditBonus = 0f;

        if (UpgradeManager != null)
        {
            GlobalCreditMultiplier = UpgradeManager.GetModifiedFloatStat(
                UpgradeStatType.OreSellValueMultiplier,
                1f);

            PerOreCreditMultiplier = UpgradeManager.GetModifiedOreFloatStat(
                UpgradeStatType.OreSellValueMultiplierPerOre,
                OreId,
                1f);

            PerOreFlatCreditBonus = UpgradeManager.GetModifiedOreFloatStat(
                UpgradeStatType.OreSellValueFlatBonusPerOre,
                OreId,
                0f);
        }

        float CreditPurityFactor = 1f + ((Purity - 1f) * PurityCreditInfluence);
        float CreditSizeFactor = 1f + ((Size - 1f) * SizeCreditInfluence);

        float WeightFactor =
            (1f + ((Size - 1f) * SizeWeightInfluence)) *
            (1f + ((Purity - 1f) * PurityWeightInfluence));

        float FinalCreditValue = CurrencyMath.RoundCurrency(
            (
                BaseCreditRoll *
                Mathf.Max(0.1f, CreditPurityFactor) *
                Mathf.Max(0.1f, CreditSizeFactor) *
                Mathf.Max(0.01f, GlobalCreditMultiplier) *
                Mathf.Max(0.01f, PerOreCreditMultiplier)
            ) + PerOreFlatCreditBonus);

        float FinalWeightValue =
            OreDefinition.GetBaseWeightValue() *
            Mathf.Max(0.1f, WeightFactor);

        OreItemData.SetCreditValue(Mathf.Max(0f, FinalCreditValue));
        OreItemData.SetWeightValue(Mathf.Max(0f, FinalWeightValue));

        if (DebugLogs)
        {
            Debug.Log(
                "[OreRuntimeService] Resolved values for " + OreDefinition.GetDisplayName() +
                " | Credits=" + FinalCreditValue.ToString("0.00") +
                " | Weight=" + FinalWeightValue.ToString("0.00") +
                " | Purity=" + Purity.ToString("F2") +
                " | Size=" + Size.ToString("F2") +
                " | OreId=" + OreId,
                this);
        }
    }

    /// <summary>
    /// Spawns a physical ore pickup carrying the provided runtime ore payload.
    /// </summary>
    /// <param name="OreItemData">Runtime ore payload to assign to the spawned pickup.</param>
    /// <param name="Position">World spawn position.</param>
    /// <param name="Rotation">World spawn rotation.</param>
    /// <returns>Spawned root GameObject or null.</returns>
    public GameObject SpawnOrePickup(OreItemData OreItemData, Vector3 Position, Quaternion Rotation)
    {
        if (OreItemData == null || OreItemData.GetOreDefinition() == null)
        {
            return null;
        }

        GameObject DroppedOrePrefab = OreItemData.GetOreDefinition().GetRandomDroppedOrePrefab();

        if (DroppedOrePrefab == null)
        {
            return null;
        }

        OrePickup OrePickup = null;

        if (OrePickupPool != null)
        {
            OrePickup = OrePickupPool.GetPickup(DroppedOrePrefab, Position, Rotation);
        }

        if (OrePickup == null)
        {
            GameObject DroppedObject = Instantiate(DroppedOrePrefab, Position, Rotation);
            OrePickup = DroppedObject.GetComponent<OrePickup>();

            if (OrePickup == null)
            {
                OrePickup = DroppedObject.GetComponentInChildren<OrePickup>(true);
            }

            if (OrePickup != null)
            {
                OrePickup.BindPool(null, DroppedOrePrefab);
            }
        }

        if (OrePickup == null)
        {
            return null;
        }

        OrePickup.Initialize(OreItemData);
        return OrePickup.GetRuntimeRoot().gameObject;
    }

    /// <summary>
    /// Applies purchased property upgrades to a generated ore property value.
    /// </summary>
    /// <param name="PropertyType">Property type being generated.</param>
    /// <param name="Value">Base generated value.</param>
    /// <returns>Modified generated value.</returns>
    private float ApplyPropertyUpgradeMultiplier(OrePropertyType PropertyType, float Value)
    {
        if (UpgradeManager == null)
        {
            return Value;
        }

        switch (PropertyType)
        {
            case OrePropertyType.Purity:
                return Value * UpgradeManager.GetModifiedFloatStat(UpgradeStatType.OrePurityMultiplier, 1f);

            case OrePropertyType.Size:
                return Value * UpgradeManager.GetModifiedFloatStat(UpgradeStatType.OreSizeMultiplier, 1f);

            default:
                return Value;
        }
    }

    /// <summary>
    /// Logs service messages if debug logging is enabled.
    /// </summary>
    /// <param name="Message">Message to write.</param>
    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[OreRuntimeService] " + Message, this);
    }
}
