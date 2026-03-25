using UnityEngine;

/// <summary>
/// Central mining resolver used to translate ore definitions into runtime results.
/// This service is intentionally compact and acts as the single place where upgrades
/// affect mining hits, respawn time, drop count, ore properties and ore value.
/// </summary>
public sealed class OreRuntimeService : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Upgrade manager used to resolve upgrade-modified mining values.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional pickup pool used to reuse dropped ore objects instead of instantiating and destroying them.")]
    [SerializeField] private OrePickupPool OrePickupPool;

    [Header("Debug")]
    [Tooltip("Logs generation and value resolution details.")]
    [SerializeField] private bool DebugLogs = false;

    public int ResolveHitsRequired(OreDefinition oreDefinition)
    {
        if (oreDefinition == null)
        {
            return 1;
        }

        int baseHitsRequired = oreDefinition.GetBaseHitsRequired();

        if (UpgradeManager == null)
        {
            return Mathf.Max(1, baseHitsRequired);
        }

        int finalHitsRequired = UpgradeManager.GetModifiedIntStat(
            UpgradeStatType.MiningHitsRequired,
            baseHitsRequired
        );

        return Mathf.Max(1, finalHitsRequired);
    }

    public float ResolveRespawnTime(OreDefinition oreDefinition)
    {
        if (oreDefinition == null)
        {
            return 0f;
        }

        float baseRespawnTime = oreDefinition.GetBaseRespawnTime();

        if (UpgradeManager == null)
        {
            return Mathf.Max(0f, baseRespawnTime);
        }

        float respawnMultiplier = UpgradeManager.GetModifiedFloatStat(
            UpgradeStatType.OreRespawnTimeMultiplier,
            1f
        );

        return Mathf.Max(0f, baseRespawnTime * Mathf.Max(0.01f, respawnMultiplier));
    }

    public int ResolveDropCount(OreDefinition oreDefinition)
    {
        if (oreDefinition == null)
        {
            return 0;
        }

        int dropCount = Random.Range(
            oreDefinition.GetBaseDropCountMin(),
            oreDefinition.GetBaseDropCountMax() + 1
        );

        if (UpgradeManager != null)
        {
            dropCount = UpgradeManager.GetModifiedIntStat(
                UpgradeStatType.OreYieldAmount,
                dropCount
            );
        }

        return Mathf.Max(0, dropCount);
    }

    public OreItemData CreateOreItemData(OreDefinition oreDefinition)
    {
        if (oreDefinition == null)
        {
            return null;
        }

        OreItemData oreItemData = new OreItemData(oreDefinition);
        var propertyRanges = oreDefinition.GetPropertyRanges();

        for (int index = 0; index < propertyRanges.Count; index++)
        {
            OreDefinition.OrePropertyRange propertyRange = propertyRanges[index];

            if (propertyRange == null || propertyRange.GetPropertyType() == OrePropertyType.None)
            {
                continue;
            }

            float randomValue = Random.Range(
                propertyRange.GetMinValue(),
                propertyRange.GetMaxValue()
            );

            if (propertyRange.GetAffectedByUpgrades())
            {
                randomValue = ApplyPropertyUpgradeMultiplier(
                    propertyRange.GetPropertyType(),
                    randomValue
                );
            }

            oreItemData.SetProperty(propertyRange.GetPropertyType(), randomValue);
        }

        ResolveOreValues(oreItemData);
        Log("Created ore data for " + oreDefinition.GetDisplayName());
        return oreItemData;
    }

    public void ResolveOreValues(OreItemData oreItemData)
    {
        if (oreItemData == null || oreItemData.GetOreDefinition() == null)
        {
            return;
        }

        OreDefinition oreDefinition = oreItemData.GetOreDefinition();

        float purityMultiplier = Mathf.Max(0.01f, oreItemData.GetPropertyValue(OrePropertyType.Purity, 1f));
        float sizeMultiplier = Mathf.Max(0.01f, oreItemData.GetPropertyValue(OrePropertyType.Size, 1f));

        float goldMultiplier = 1f;
        float researchMultiplier = 1f;

        if (UpgradeManager != null)
        {
            goldMultiplier = UpgradeManager.GetModifiedFloatStat(
                UpgradeStatType.OreSellValueMultiplier,
                1f
            );

            researchMultiplier = UpgradeManager.GetModifiedFloatStat(
                UpgradeStatType.ResearchSellValueMultiplier,
                1f
            );
        }

        int finalGoldValue = Mathf.RoundToInt(
            oreDefinition.GetBaseGoldValue() *
            purityMultiplier *
            sizeMultiplier *
            goldMultiplier
        );

        int finalWeightValue = Mathf.RoundToInt(
            oreDefinition.GetBaseWeightValue() *
            sizeMultiplier * purityMultiplier
        );

        int finalResearchValue = Mathf.RoundToInt(
            oreDefinition.GetBaseResearchValue() *
            purityMultiplier *
            sizeMultiplier *
            researchMultiplier
        );

        oreItemData.SetGoldValue(Mathf.Max(0, finalGoldValue));
        oreItemData.SetResearchValue(Mathf.Max(0, finalResearchValue));
        oreItemData.SetWeightValue(Mathf.Max(0, finalWeightValue));
    }

    /// <summary>
    /// Spawns one dropped ore pickup using the configured pool when available.
    /// Falls back to regular instantiation if no pool has been assigned.
    /// </summary>
    public GameObject SpawnOrePickup(OreItemData oreItemData, Vector3 position, Quaternion rotation)
    {
        if (oreItemData == null || oreItemData.GetOreDefinition() == null)
        {
            return null;
        }

        GameObject droppedOrePrefab = oreItemData.GetOreDefinition().GetDroppedOrePrefab();

        if (droppedOrePrefab == null)
        {
            return null;
        }

        OrePickup orePickup = null;

        if (OrePickupPool != null)
        {
            orePickup = OrePickupPool.GetPickup(droppedOrePrefab, position, rotation);
        }

        if (orePickup == null)
        {
            GameObject droppedObject = Instantiate(droppedOrePrefab, position, rotation);
            orePickup = droppedObject.GetComponent<OrePickup>();

            if (orePickup == null)
            {
                orePickup = droppedObject.GetComponentInChildren<OrePickup>();
            }
        }

        if (orePickup == null)
        {
            return null;
        }

        orePickup.Initialize(oreItemData);
        return orePickup.GetRuntimeRoot().gameObject;
    }

    private float ApplyPropertyUpgradeMultiplier(OrePropertyType propertyType, float value)
    {
        if (UpgradeManager == null)
        {
            return value;
        }

        switch (propertyType)
        {
            case OrePropertyType.Purity:
                return value * UpgradeManager.GetModifiedFloatStat(UpgradeStatType.OrePurityMultiplier, 1f);

            case OrePropertyType.Size:
                return value * UpgradeManager.GetModifiedFloatStat(UpgradeStatType.OreSizeMultiplier, 1f);

            default:
                return value;
        }
    }

    private void Log(string message)
    {
        if (!DebugLogs)
        {
            return;
        }

        Debug.Log("[OreRuntimeService] " + message);
    }
}
