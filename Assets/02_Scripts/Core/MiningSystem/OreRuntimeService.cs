using UnityEngine;

/// <summary>
/// Central mining resolver used to translate ore definitions into runtime results.
/// This service acts as the single place where upgrades affect mining hits,
/// respawn time, drop count, ore properties and ore value.
/// </summary>
public sealed class OreRuntimeService : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UpgradeManager UpgradeManager;
    [SerializeField] private OrePickupPool OrePickupPool;

    [Header("Economy Influence")]
    [SerializeField] private float PurityGoldInfluence = 0.35f;
    [SerializeField] private float SizeGoldInfluence = 0.25f;
    [SerializeField] private float PurityResearchInfluence = 0.20f;
    [SerializeField] private float SizeResearchInfluence = 0.10f;
    [SerializeField] private float SizeWeightInfluence = 0.75f;
    [SerializeField] private float PurityWeightInfluence = 0.10f;

    [Header("Debug")]
    [SerializeField] private bool DebugLogs = false;

    public int ResolveHitsRequired(OreDefinition OreDefinition)
    {
        if (OreDefinition == null)
        {
            return 1;
        }

        int BaseHitsRequired = OreDefinition.GetBaseHitsRequired();

        if (UpgradeManager == null)
        {
            return Mathf.Max(1, BaseHitsRequired);
        }

        int FinalHitsRequired = UpgradeManager.GetModifiedOreIntStat(
            UpgradeStatType.MiningHitsRequired,
            OreDefinition.GetOreId(),
            BaseHitsRequired
        );

        return Mathf.Max(1, FinalHitsRequired);
    }

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
            1f
        );

        return Mathf.Max(0f, BaseRespawnTime * Mathf.Max(0.01f, RespawnMultiplier));
    }

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
                FinalDropCountMin
            );

            FinalDropCountMax = UpgradeManager.GetModifiedOreIntStat(
                UpgradeStatType.OreYieldAmountMax,
                OreDefinition.GetOreId(),
                FinalDropCountMax
            );
        }

        FinalDropCountMin = Mathf.Max(0, FinalDropCountMin);
        FinalDropCountMax = Mathf.Max(FinalDropCountMin, FinalDropCountMax);

        return Random.Range(FinalDropCountMin, FinalDropCountMax + 1);
    }

    public OreItemData CreateOreItemData(OreDefinition OreDefinition)
    {
        if (OreDefinition == null)
        {
            return null;
        }

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
                PropertyRange.GetMaxValue()
            );

            if (PropertyRange.GetAffectedByUpgrades())
            {
                RandomValue = ApplyPropertyUpgradeMultiplier(
                    PropertyRange.GetPropertyType(),
                    RandomValue
                );
            }

            OreItemData.SetProperty(PropertyRange.GetPropertyType(), RandomValue);
        }

        ResolveOreValues(OreItemData);

        Log("Created ore data for " + OreDefinition.GetDisplayName());
        return OreItemData;
    }

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

        float BaseGoldRoll = CurrencyMath.RoundCurrency(Random.Range(
            OreDefinition.GetBaseGoldValueMin(),
            OreDefinition.GetBaseGoldValueMax()
        ));

        float BaseResearchRoll = CurrencyMath.RoundCurrency(Random.Range(
            OreDefinition.GetBaseResearchValueMin(),
            OreDefinition.GetBaseResearchValueMax()
        ));

        float GlobalGoldMultiplier = 1f;
        float PerOreGoldMultiplier = 1f;
        float PerOreFlatGoldBonus = 0f;
        float ResearchMultiplier = 1f;

        if (UpgradeManager != null)
        {
            GlobalGoldMultiplier = UpgradeManager.GetModifiedFloatStat(
                UpgradeStatType.OreSellValueMultiplier,
                1f
            );

            PerOreGoldMultiplier = UpgradeManager.GetModifiedOreFloatStat(
                UpgradeStatType.OreSellValueMultiplierPerOre,
                OreId,
                1f
            );

            PerOreFlatGoldBonus = UpgradeManager.GetModifiedOreFloatStat(
                UpgradeStatType.OreSellValueFlatBonusPerOre,
                OreId,
                0f
            );

            ResearchMultiplier = UpgradeManager.GetModifiedFloatStat(
                UpgradeStatType.ResearchSellValueMultiplier,
                1f
            );
        }

        float GoldPurityFactor = 1f + ((Purity - 1f) * PurityGoldInfluence);
        float GoldSizeFactor = 1f + ((Size - 1f) * SizeGoldInfluence);

        float ResearchPurityFactor = 1f + ((Purity - 1f) * PurityResearchInfluence);
        float ResearchSizeFactor = 1f + ((Size - 1f) * SizeResearchInfluence);

        float WeightFactor =
            (1f + ((Size - 1f) * SizeWeightInfluence)) *
            (1f + ((Purity - 1f) * PurityWeightInfluence));

        float FinalGoldValue = CurrencyMath.RoundCurrency(
            (
                BaseGoldRoll *
                Mathf.Max(0.1f, GoldPurityFactor) *
                Mathf.Max(0.1f, GoldSizeFactor) *
                Mathf.Max(0.01f, GlobalGoldMultiplier) *
                Mathf.Max(0.01f, PerOreGoldMultiplier)
            ) + PerOreFlatGoldBonus
        );

        float FinalResearchValue = CurrencyMath.RoundCurrency(
            BaseResearchRoll *
            Mathf.Max(0.1f, ResearchPurityFactor) *
            Mathf.Max(0.1f, ResearchSizeFactor) *
            Mathf.Max(0.01f, ResearchMultiplier)
        );

        float FinalWeightValue =
            OreDefinition.GetBaseWeightValue() *
            Mathf.Max(0.1f, WeightFactor);

        OreItemData.SetGoldValue(Mathf.Max(0f, FinalGoldValue));
        OreItemData.SetResearchValue(Mathf.Max(0f, FinalResearchValue));
        OreItemData.SetWeightValue(Mathf.Max(0f, FinalWeightValue));

        if (DebugLogs)
        {
            Debug.Log(
                "[OreRuntimeService] Resolved values for " + OreDefinition.GetDisplayName() +
                " | Gold=" + FinalGoldValue.ToString("0.00") +
                " | Research=" + FinalResearchValue.ToString("0.00") +
                " | Weight=" + FinalWeightValue.ToString("0.00") +
                " | Purity=" + Purity.ToString("F2") +
                " | Size=" + Size.ToString("F2") +
                " | OreId=" + OreId,
                this);
        }
    }

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
                OrePickup = DroppedObject.GetComponentInChildren<OrePickup>();
            }
        }

        if (OrePickup == null)
        {
            return null;
        }

        OrePickup.Initialize(OreItemData);
        return OrePickup.GetRuntimeRoot().gameObject;
    }

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

    private void Log(string Message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[OreRuntimeService] " + Message, this);
    }
}