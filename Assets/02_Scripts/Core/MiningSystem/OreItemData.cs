using System;
using UnityEngine;

/// <summary>
/// Runtime ore payload generated when a vein breaks.
/// This data travels with the dropped physical ore object and is later used for selling, scanning, research filtering or physical weight evaluation.
/// </summary>
[Serializable]
public sealed class OreItemData
{
    [Tooltip("Static ore definition used to create this runtime payload.")]
    [SerializeField] private OreDefinition OreDefinition;

    [Tooltip("Generated ore purity expressed as a percent from 0 to 100.")]
    [SerializeField] private float PurityPercent;

    [Tooltip("Generated ore size expressed as natural scale. 1 is normal size, 0.5 is half size and 2 is double size.")]
    [SerializeField] private float SizeScale = 1f;

    [Tooltip("Final credit value received when this ore is sold.")]
    [SerializeField] private float CreditValue;

    [Tooltip("Final physical weight contributed by this ore.")]
    [SerializeField] private float WeightValue;

    [Tooltip("True after the ore has already been processed by the future purity machine.")]
    [SerializeField] private bool HasBeenPurityProcessed;

    /// <summary>
    /// Initializes a new runtime ore payload from the provided static ore definition.
    /// </summary>
    /// <param name="OreDefinitionValue">Static ore definition used by this runtime payload.</param>
    public OreItemData(OreDefinition OreDefinitionValue)
    {
        OreDefinition = OreDefinitionValue;
    }

    /// <summary>
    /// Gets the static ore definition used by this runtime payload.
    /// </summary>
    public OreDefinition GetOreDefinition() => OreDefinition;

    /// <summary>
    /// Gets the generated ore purity as a percent from 0 to 100.
    /// </summary>
    public float GetPurityPercent() => Mathf.Clamp(PurityPercent, 0f, 100f);

    /// <summary>
    /// Sets the generated ore purity as a percent from 0 to 100.
    /// </summary>
    /// <param name="PurityPercentValue">New purity percent.</param>
    public void SetPurityPercent(float PurityPercentValue)
    {
        PurityPercent = Mathf.Clamp(PurityPercentValue, 0f, 100f);
    }

    /// <summary>
    /// Gets the generated ore purity normalized to 0..1 for formulas.
    /// </summary>
    public float GetPurity01() => Mathf.Clamp01(GetPurityPercent() / 100f);

    /// <summary>
    /// Gets the generated ore size as natural scale.
    /// </summary>
    public float GetSizeScale() => Mathf.Max(0.01f, SizeScale);

    /// <summary>
    /// Sets the generated ore size as natural scale.
    /// </summary>
    /// <param name="SizeScaleValue">New natural scale value.</param>
    public void SetSizeScale(float SizeScaleValue)
    {
        SizeScale = Mathf.Max(0.01f, SizeScaleValue);
    }

    /// <summary>
    /// Gets the generated ore size normalized inside the static definition range.
    /// </summary>
    public float GetSize01()
    {
        if (OreDefinition == null)
        {
            return Mathf.Clamp01(GetSizeScale());
        }

        return OreDefinition.NormalizeSizeScale(GetSizeScale());
    }

    /// <summary>
    /// Gets the final credit value received when this ore is sold.
    /// </summary>
    public float GetCreditValue() => CreditValue;

    /// <summary>
    /// Sets the final credit value received when this ore is sold.
    /// </summary>
    /// <param name="CreditValueAmount">Final credit value.</param>
    public void SetCreditValue(float CreditValueAmount) => CreditValue = CurrencyMath.RoundCurrency(Mathf.Max(0f, CreditValueAmount));

    /// <summary>
    /// Sets the final physical weight contributed by this ore.
    /// </summary>
    /// <param name="WeightValueAmount">Final physical weight.</param>
    public void SetWeightValue(float WeightValueAmount) => WeightValue = Mathf.Max(0f, WeightValueAmount);

    /// <summary>
    /// Gets the final physical weight contributed by this ore.
    /// </summary>
    public float GetWeightValue() => WeightValue;

    /// <summary>
    /// Gets whether this ore has already been processed by the future purity machine.
    /// </summary>
    public bool GetHasBeenPurityProcessed() => HasBeenPurityProcessed;

    /// <summary>
    /// Sets whether this ore has already been processed by the future purity machine.
    /// </summary>
    /// <param name="IsProcessed">True when this ore has been processed.</param>
    public void SetHasBeenPurityProcessed(bool IsProcessed) => HasBeenPurityProcessed = IsProcessed;
}
