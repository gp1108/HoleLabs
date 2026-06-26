using UnityEngine;

/// <summary>
/// Central mining resolver used to translate ore definitions into runtime ore payloads.
/// This service is the authoritative place where upgrades affect mining durability, respawn time, drop count, ore purity, ore size, credit value and physical weight.
/// </summary>
public sealed class OreRuntimeService : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Runtime upgrade manager used to resolve purchased modifiers.")]
    [SerializeField] private UpgradeManager UpgradeManager;

    [Tooltip("Optional ore pickup pool used when spawning dropped physical ores.")]
    [SerializeField] private OrePickupPool OrePickupPool;

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
            UpgradeStatType.MiningDurabilityRequired,
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
        ResolveDropCountRange(OreDefinition, out int FinalDropCountMin, out int FinalDropCountMax);
        return Random.Range(FinalDropCountMin, FinalDropCountMax + 1);
    }

    /// <summary>
    /// Resolves the upgraded drop count range for an ore definition without rolling the final random value.
    /// Scene-authored vein size profiles use this to decide whether to use the minimum, maximum, random range or an additive variant.
    /// </summary>
    /// <param name="OreDefinition">Ore definition to resolve.</param>
    /// <param name="FinalDropCountMin">Resolved minimum drop count.</param>
    /// <param name="FinalDropCountMax">Resolved maximum drop count.</param>
    public void ResolveDropCountRange(OreDefinition OreDefinition, out int FinalDropCountMin, out int FinalDropCountMax)
    {
        if (OreDefinition == null)
        {
            FinalDropCountMin = 0;
            FinalDropCountMax = 0;
            return;
        }

        FinalDropCountMin = OreDefinition.GetBaseDropCountMin();
        FinalDropCountMax = OreDefinition.GetBaseDropCountMax();

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
    }

    /// <summary>
    /// Creates a runtime ore payload from a static ore definition with no tool purity bonus.
    /// </summary>
    /// <param name="OreDefinition">Ore definition used to create the runtime payload.</param>
    /// <returns>Runtime ore payload or null.</returns>
    public OreItemData CreateOreItemData(OreDefinition OreDefinition)
    {
        return CreateOreItemData(OreDefinition, 0f, 0f);
    }

    /// <summary>
    /// Creates a runtime ore payload from a static ore definition and adds a flat purity bonus rolled from the provided range.
    /// Purity is stored as 0..100 percent and size is stored as natural scale.
    /// </summary>
    /// <param name="OreDefinition">Ore definition used to create the runtime payload.</param>
    /// <param name="PurityBonusPercentMin">Minimum flat purity percent bonus added after the ore base purity roll.</param>
    /// <param name="PurityBonusPercentMax">Maximum flat purity percent bonus added after the ore base purity roll.</param>
    /// <returns>Runtime ore payload or null.</returns>
    public OreItemData CreateOreItemData(OreDefinition OreDefinition, float PurityBonusPercentMin, float PurityBonusPercentMax)
    {
        if (OreDefinition == null)
        {
            return null;
        }

        OreItemData OreItemData = new OreItemData(OreDefinition);

        float BasePurityPercent = Random.Range(
            OreDefinition.GetMinPurityPercent(),
            OreDefinition.GetMaxPurityPercent());

        float SafeBonusMin = Mathf.Min(PurityBonusPercentMin, PurityBonusPercentMax);
        float SafeBonusMax = Mathf.Max(PurityBonusPercentMin, PurityBonusPercentMax);
        float RolledPurityBonusPercent = Random.Range(SafeBonusMin, SafeBonusMax);
        float PurityPercent = ApplyPurityUpgradeMultiplier(BasePurityPercent + RolledPurityBonusPercent);
        OreItemData.SetPurityPercent(PurityPercent);

        float SizeScale = Random.Range(
            OreDefinition.GetMinSizeScale(),
            OreDefinition.GetMaxSizeScale());

        SizeScale = ApplySizeUpgradeMultiplier(SizeScale);
        OreItemData.SetSizeScale(SizeScale);
        OreItemData.SetHasBeenPurityProcessed(false);

        ResolveOreValues(OreItemData);

        Log("Created ore data for " + OreDefinition.GetDisplayName() +
            " | BasePurity=" + BasePurityPercent.ToString("0.##") + "%" +
            " | ToolPurityBonus=" + RolledPurityBonusPercent.ToString("0.##") + "%" +
            " | FinalPurity=" + OreItemData.GetPurityPercent().ToString("0.##") + "%" +
            " | Size=" + OreItemData.GetSizeScale().ToString("0.##"));

        return OreItemData;
    }

    /// <summary>
    /// Resolves final credit value and physical weight for a runtime ore payload.
    /// Credit and weight both interpolate between ore definition min/max values using explicit purity and size contributions.
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

        float Purity01 = OreItemData.GetPurity01();
        float Size01 = OreItemData.GetSize01();

        float CreditInfluence01 = ResolveWeightedInfluence01(
            Purity01,
            Size01,
            OreDefinition.GetPurityCreditContribution(),
            OreDefinition.GetSizeCreditContribution(),
            "credit value",
            OreDefinition);

        float WeightInfluence01 = ResolveWeightedInfluence01(
            Purity01,
            Size01,
            OreDefinition.GetPurityWeightContribution(),
            OreDefinition.GetSizeWeightContribution(),
            "weight",
            OreDefinition);

        float BaseCreditValue = CurrencyMath.RoundCurrency(Mathf.Lerp(
            OreDefinition.GetMinCreditValue(),
            OreDefinition.GetMaxCreditValue(),
            CreditInfluence01));

        float FinalWeightValue = Mathf.Lerp(
            OreDefinition.GetMinWeightValue(),
            OreDefinition.GetMaxWeightValue(),
            WeightInfluence01);

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

        float FinalCreditValue = CurrencyMath.RoundCurrency(
            (BaseCreditValue * Mathf.Max(0.01f, GlobalCreditMultiplier) * Mathf.Max(0.01f, PerOreCreditMultiplier)) +
            PerOreFlatCreditBonus);

        OreItemData.SetCreditValue(Mathf.Max(0f, FinalCreditValue));
        OreItemData.SetWeightValue(Mathf.Max(0f, FinalWeightValue));

        if (DebugLogs)
        {
            Debug.Log(
                "[OreRuntimeService] Resolved values for " + OreDefinition.GetDisplayName() +
                " | Credits=" + FinalCreditValue.ToString("0.00") +
                " | Weight=" + FinalWeightValue.ToString("0.00") +
                " | Purity=" + OreItemData.GetPurityPercent().ToString("0.##") + "%" +
                " | Size=" + OreItemData.GetSizeScale().ToString("0.##") +
                " | CreditInfluence=" + CreditInfluence01.ToString("0.00") +
                " | WeightInfluence=" + WeightInfluence01.ToString("0.00") +
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
    /// Applies purchased purity upgrades to generated purity percent after tool purity bonuses have been added.
    /// </summary>
    /// <param name="PurityPercent">Generated purity percent before global upgrades.</param>
    /// <returns>Upgraded and clamped purity percent.</returns>
    private float ApplyPurityUpgradeMultiplier(float PurityPercent)
    {
        float Multiplier = UpgradeManager != null
            ? UpgradeManager.GetModifiedFloatStat(UpgradeStatType.OrePurityMultiplier, 1f)
            : 1f;

        return Mathf.Clamp(PurityPercent * Mathf.Max(0.01f, Multiplier), 0f, 100f);
    }

    /// <summary>
    /// Applies purchased size upgrades to generated natural size scale.
    /// </summary>
    /// <param name="SizeScale">Generated natural size scale before global upgrades.</param>
    /// <returns>Upgraded size scale.</returns>
    private float ApplySizeUpgradeMultiplier(float SizeScale)
    {
        float Multiplier = UpgradeManager != null
            ? UpgradeManager.GetModifiedFloatStat(UpgradeStatType.OreSizeMultiplier, 1f)
            : 1f;

        return Mathf.Max(0.01f, SizeScale * Mathf.Max(0.01f, Multiplier));
    }

    /// <summary>
    /// Resolves a normalized influence value from purity and size using their explicit contributions.
    /// </summary>
    /// <param name="Purity01">Purity normalized to 0..1.</param>
    /// <param name="Size01">Size normalized to 0..1.</param>
    /// <param name="PurityContribution">Contribution weight for purity.</param>
    /// <param name="SizeContribution">Contribution weight for size.</param>
    /// <param name="TargetLabel">Debug label describing the target value.</param>
    /// <param name="OreDefinition">Ore definition being evaluated.</param>
    /// <returns>Weighted normalized influence.</returns>
    private float ResolveWeightedInfluence01(
        float Purity01,
        float Size01,
        float PurityContribution,
        float SizeContribution,
        string TargetLabel,
        OreDefinition OreDefinition)
    {
        float SafePurityContribution = Mathf.Max(0f, PurityContribution);
        float SafeSizeContribution = Mathf.Max(0f, SizeContribution);
        float ContributionTotal = SafePurityContribution + SafeSizeContribution;

        if (ContributionTotal <= Mathf.Epsilon)
        {
            if (DebugLogs && OreDefinition != null)
            {
                Debug.LogWarning(
                    "[OreRuntimeService] Ore " + OreDefinition.GetDisplayName() +
                    " has no purity or size contribution for " + TargetLabel +
                    ". The minimum value will be used.",
                    this);
            }

            return 0f;
        }

        return Mathf.Clamp01(
            ((Mathf.Clamp01(Purity01) * SafePurityContribution) +
             (Mathf.Clamp01(Size01) * SafeSizeContribution)) /
            ContributionTotal);
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
