using UnityEngine;

/// <summary>
/// Immutable request sent by any mining source to a mineable target.
/// It contains the source context, damage, tier and extraction quality required to resolve the hit deterministically.
/// </summary>
public readonly struct MiningHitRequest
{
    /// <summary>
    /// Damage applied to the target ore durability when the hit is accepted.
    /// </summary>
    public readonly float MiningDamage;

    /// <summary>
    /// Mining tier provided by the tool or machine that caused the hit.
    /// </summary>
    public readonly MiningTier MiningTier;

    /// <summary>
    /// Multiplier applied to generated ore purity when the hit breaks the vein.
    /// Values below one represent rough extraction. Values above one preserve or improve extraction quality.
    /// </summary>
    public readonly float ExtractionQualityMultiplier;

    /// <summary>
    /// Durability consumed by the source tool if the hit is accepted.
    /// The mineable target does not spend durability directly; the tool consumes this after receiving the result.
    /// </summary>
    public readonly float DurabilityCost;

    /// <summary>
    /// World context of the mining hit.
    /// </summary>
    public readonly MiningHitContext HitContext;

    /// <summary>
    /// Creates a new mining hit request.
    /// </summary>
    /// <param name="MiningDamageValue">Raw mining damage applied by the source.</param>
    /// <param name="MiningTierValue">Mining tier provided by the source.</param>
    /// <param name="ExtractionQualityMultiplierValue">Extraction quality multiplier applied to drops when this hit breaks the vein.</param>
    /// <param name="DurabilityCostValue">Durability cost consumed by the source tool after an accepted hit.</param>
    /// <param name="HitContextValue">World hit context.</param>
    public MiningHitRequest(
        float MiningDamageValue,
        MiningTier MiningTierValue,
        float ExtractionQualityMultiplierValue,
        float DurabilityCostValue,
        MiningHitContext HitContextValue)
    {
        MiningDamage = Mathf.Max(0f, MiningDamageValue);
        MiningTier = MiningTierValue;
        ExtractionQualityMultiplier = Mathf.Max(0.01f, ExtractionQualityMultiplierValue);
        DurabilityCost = Mathf.Max(0f, DurabilityCostValue);
        HitContext = HitContextValue;
    }

    /// <summary>
    /// Creates a backwards-compatible mining request from the old mining power call path.
    /// </summary>
    /// <param name="MiningPower">Legacy mining power value.</param>
    /// <param name="HitContext">World hit context.</param>
    /// <returns>Mining request using tier I and neutral extraction quality.</returns>
    public static MiningHitRequest CreateLegacy(float MiningPower, MiningHitContext HitContext)
    {
        return new MiningHitRequest(
            Mathf.Max(1f, MiningPower),
            MiningTier.TierI,
            1f,
            0f,
            HitContext);
    }
}
