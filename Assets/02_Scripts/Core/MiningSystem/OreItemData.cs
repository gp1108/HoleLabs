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
        [Tooltip("Type of ore property stored by this entry.")]
        [SerializeField] private OrePropertyType PropertyType = OrePropertyType.None;

        [Tooltip("Runtime numeric value of the property.")]
        [SerializeField] private float Value = 0f;

        public OrePropertyValue(OrePropertyType propertyType, float value)
        {
            PropertyType = propertyType;
            Value = value;
        }

        public OrePropertyType GetPropertyType() { return PropertyType; }
        public float GetValue() { return Value; }
        public void SetValue(float value) { Value = value; }
    }

    [Tooltip("Static ore definition used to identify this runtime payload.")]
    [SerializeField] private OreDefinition OreDefinition;

    [Tooltip("Generated ore properties such as purity and size.")]
    [SerializeField] private List<OrePropertyValue> Properties = new();

    [Tooltip("Resolved gold value of this ore payload.")]
    [SerializeField] private int GoldValue;

    [Tooltip("Resolved research value of this ore payload.")]
    [SerializeField] private int ResearchValue;

    public OreItemData(OreDefinition oreDefinition)
    {
        OreDefinition = oreDefinition;
    }

    public OreDefinition GetOreDefinition() { return OreDefinition; }
    public int GetGoldValue() { return GoldValue; }
    public void SetGoldValue(int goldValue) { GoldValue = Mathf.Max(0, goldValue); }
    public int GetResearchValue() { return ResearchValue; }
    public void SetResearchValue(int researchValue) { ResearchValue = Mathf.Max(0, researchValue); }
    public IReadOnlyList<OrePropertyValue> GetProperties() { return Properties; }

    public void SetProperty(OrePropertyType propertyType, float value)
    {
        for (int index = 0; index < Properties.Count; index++)
        {
            if (Properties[index].GetPropertyType() != propertyType)
            {
                continue;
            }

            Properties[index].SetValue(value);
            return;
        }

        Properties.Add(new OrePropertyValue(propertyType, value));
    }

    public float GetPropertyValue(OrePropertyType propertyType, float fallbackValue = 0f)
    {
        for (int index = 0; index < Properties.Count; index++)
        {
            if (Properties[index].GetPropertyType() == propertyType)
            {
                return Properties[index].GetValue();
            }
        }

        return fallbackValue;
    }
}
