using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime ore payload generated when a vein breaks.
/// This data travels with the dropped physical ore object and is later used for selling or analysis.
/// </summary>
[Serializable]
public sealed class OreItemData
{
    [Serializable]
    public sealed class OrePropertyValue
    {
        [SerializeField] private OrePropertyType PropertyType = OrePropertyType.None;
        [SerializeField] private float Value = 0f;

        public OrePropertyValue(OrePropertyType PropertyTypeValue, float ValueAmount)
        {
            PropertyType = PropertyTypeValue;
            Value = ValueAmount;
        }

        public OrePropertyType GetPropertyType() => PropertyType;
        public float GetValue() => Value;
        public void SetValue(float ValueAmount) => Value = ValueAmount;
    }

    [SerializeField] private OreDefinition OreDefinition;
    [SerializeField] private List<OrePropertyValue> Properties = new();
    [SerializeField] private float GoldValue;
    [SerializeField] private float ResearchValue;
    [SerializeField] private float WeightValue;

    public OreItemData(OreDefinition OreDefinitionValue)
    {
        OreDefinition = OreDefinitionValue;
    }

    public OreDefinition GetOreDefinition() => OreDefinition;
    public float GetGoldValue() => GoldValue;
    public void SetGoldValue(float GoldValueValue) => GoldValue = CurrencyMath.RoundCurrency(Mathf.Max(0f, GoldValueValue));
    public float GetResearchValue() => ResearchValue;
    public void SetResearchValue(float ResearchValueValue) => ResearchValue = CurrencyMath.RoundCurrency(Mathf.Max(0f, ResearchValueValue));
    public void SetWeightValue(float WeightValueAmount) => WeightValue = Mathf.Max(0f, WeightValueAmount);
    public float GetWeightValue() => WeightValue;
    public IReadOnlyList<OrePropertyValue> GetProperties() => Properties;

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