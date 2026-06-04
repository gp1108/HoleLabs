using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Runtime ore payload generated when a vein breaks.
/// This data travels with the dropped physical ore object and is later used for selling, scanning or physical weight evaluation.
/// </summary>
[Serializable]
public sealed class OreItemData
{
    [Serializable]
    public sealed class OrePropertyValue
    {
        [Tooltip("Runtime property represented by this value.")]
        [SerializeField] private OrePropertyType PropertyType = OrePropertyType.None;

        [Tooltip("Generated runtime value for this property.")]
        [SerializeField] private float Value = 0f;

        /// <summary>
        /// Initializes one runtime ore property value.
        /// </summary>
        /// <param name="PropertyTypeValue">Property type represented by this value.</param>
        /// <param name="ValueAmount">Runtime value assigned to this property.</param>
        public OrePropertyValue(OrePropertyType PropertyTypeValue, float ValueAmount)
        {
            PropertyType = PropertyTypeValue;
            Value = ValueAmount;
        }

        /// <summary>
        /// Gets the property type represented by this value.
        /// </summary>
        public OrePropertyType GetPropertyType() => PropertyType;

        /// <summary>
        /// Gets the runtime property value.
        /// </summary>
        public float GetValue() => Value;

        /// <summary>
        /// Sets the runtime property value.
        /// </summary>
        /// <param name="ValueAmount">New runtime property value.</param>
        public void SetValue(float ValueAmount) => Value = ValueAmount;
    }

    [Tooltip("Static ore definition used to create this runtime payload.")]
    [SerializeField] private OreDefinition OreDefinition;

    [Tooltip("Generated runtime ore properties such as purity and size.")]
    [SerializeField] private List<OrePropertyValue> Properties = new();

    [Tooltip("Final credit value received when this ore is sold.")]
    [FormerlySerializedAs("GoldValue")]
    [SerializeField] private float CreditValue;

    [Tooltip("Legacy research value kept only for save compatibility during migration.")]
    [SerializeField, HideInInspector] private float ResearchValue;

    [Tooltip("Final physical weight contributed by this ore.")]
    [SerializeField] private float WeightValue;

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
    /// Gets the final credit value received when this ore is sold.
    /// </summary>
    public float GetCreditValue() => CreditValue;

    /// <summary>
    /// Sets the final credit value received when this ore is sold.
    /// </summary>
    /// <param name="CreditValueAmount">Final credit value.</param>
    public void SetCreditValue(float CreditValueAmount) => CreditValue = CurrencyMath.RoundCurrency(Mathf.Max(0f, CreditValueAmount));

    /// <summary>
    /// Legacy alias for older systems. Use GetCreditValue instead.
    /// </summary>
    [Obsolete("Use GetCreditValue instead.")]
    public float GetGoldValue() => GetCreditValue();

    /// <summary>
    /// Legacy alias for older systems. Use SetCreditValue instead.
    /// </summary>
    /// <param name="GoldValueValue">Legacy gold value mapped to credits.</param>
    [Obsolete("Use SetCreditValue instead.")]
    public void SetGoldValue(float GoldValueValue) => SetCreditValue(GoldValueValue);

    /// <summary>
    /// Gets the legacy research value stored on this ore.
    /// This is retained only for migration and should not drive new gameplay.
    /// </summary>
    [Obsolete("Research currency is legacy. Use physical mineral research costs instead.")]
    public float GetResearchValue() => ResearchValue;

    /// <summary>
    /// Sets the legacy research value stored on this ore.
    /// This is retained only for migration and should not drive new gameplay.
    /// </summary>
    /// <param name="ResearchValueValue">Legacy research value.</param>
    [Obsolete("Research currency is legacy. Use physical mineral research costs instead.")]
    public void SetResearchValue(float ResearchValueValue) => ResearchValue = CurrencyMath.RoundCurrency(Mathf.Max(0f, ResearchValueValue));


    /// <summary>
    /// Clears the legacy abstract research value during migration.
    /// New research progression should consume physical minerals instead of reading this value.
    /// </summary>
    public void ClearLegacyResearchValue() => ResearchValue = 0f;

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
    /// Gets all generated runtime properties stored by this ore.
    /// </summary>
    public IReadOnlyList<OrePropertyValue> GetProperties() => Properties;

    /// <summary>
    /// Sets or replaces a generated runtime property value.
    /// </summary>
    /// <param name="PropertyTypeValue">Property type to set.</param>
    /// <param name="Value">Runtime value to assign.</param>
    public void SetProperty(OrePropertyType PropertyTypeValue, float Value)
    {
        for (int Index = 0; Index < Properties.Count; Index++)
        {
            if (Properties[Index].GetPropertyType() != PropertyTypeValue)
            {
                continue;
            }

            Properties[Index].SetValue(Value);
            return;
        }

        Properties.Add(new OrePropertyValue(PropertyTypeValue, Value));
    }

    /// <summary>
    /// Gets a generated runtime property value.
    /// </summary>
    /// <param name="PropertyTypeValue">Property type to read.</param>
    /// <param name="FallbackValue">Returned value when the property is not present.</param>
    /// <returns>Runtime property value or fallback.</returns>
    public float GetPropertyValue(OrePropertyType PropertyTypeValue, float FallbackValue = 0f)
    {
        for (int Index = 0; Index < Properties.Count; Index++)
        {
            if (Properties[Index].GetPropertyType() == PropertyTypeValue)
            {
                return Properties[Index].GetValue();
            }
        }

        return FallbackValue;
    }
}
