using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "OreDefinition_", menuName = "Game/Mining/Ore Definition")]
public sealed class OreDefinition : ScriptableObject
{
    [Serializable]
    public sealed class OrePropertyRange
    {
        [Tooltip("Type of ore property generated from this range.")]
        [SerializeField] private OrePropertyType PropertyType = OrePropertyType.None;

        [Tooltip("Minimum random value generated for this property.")]
        [SerializeField] private float MinValue = 0f;

        [Tooltip("Maximum random value generated for this property.")]
        [SerializeField] private float MaxValue = 1f;

        [Tooltip("If true, the generated value is multiplied by the relevant upgrade multiplier when available.")]
        [SerializeField] private bool AffectedByUpgrades = true;

        /// <summary>
        /// Gets the property type represented by this range.
        /// </summary>
        public OrePropertyType GetPropertyType()
        {
            return PropertyType;
        }

        /// <summary>
        /// Gets the minimum configured value.
        /// </summary>
        public float GetMinValue()
        {
            return Mathf.Min(MinValue, MaxValue);
        }

        /// <summary>
        /// Gets the maximum configured value.
        /// </summary>
        public float GetMaxValue()
        {
            return Mathf.Max(MinValue, MaxValue);
        }

        /// <summary>
        /// Gets whether this property should be affected by upgrade multipliers.
        /// </summary>
        public bool GetAffectedByUpgrades()
        {
            return AffectedByUpgrades;
        }
    }

    [Header("Identity")]
    [Tooltip("Unique identifier used by systems and save data.")]
    [SerializeField] private string OreId;

    [Tooltip("Display name shown in UI.")]
    [SerializeField] private string DisplayName;

    [Tooltip("Optional icon used by UI.")]
    [SerializeField] private Sprite Icon;

    [Header("World")]
    [Tooltip("Prefab spawned as the mineable ore vein.")]
    [SerializeField] private GameObject VeinPrefab;

    [Tooltip("Prefab spawned as the dropped physical ore piece.")]
    [SerializeField] private GameObject DroppedOrePrefab;

    [Header("Mining")]
    [Tooltip("Base amount of hits required before the vein breaks.")]
    [SerializeField] private int BaseHitsRequired = 3;

    [Tooltip("Base time required for the vein to fully regrow after being mined.")]
    [SerializeField] private float BaseRespawnTime = 30f;

    [Tooltip("Base minimum amount of ore pieces dropped when the vein breaks.")]
    [SerializeField] private int BaseDropCountMin = 1;

    [Tooltip("Base maximum amount of ore pieces dropped when the vein breaks.")]
    [SerializeField] private int BaseDropCountMax = 2;

    [Header("Weight")]
    [Tooltip("Base weight value in KG before property and upgrade modifiers are applied.")]
    [SerializeField] private int BaseWeightValue = 10;

    [Header("Economy")]
    [Tooltip("Base minimum gold value before property and upgrade modifiers are applied.")]
    [SerializeField] private int BaseGoldValueMin = 3;

    [Tooltip("Base maximum gold value before property and upgrade modifiers are applied.")]
    [SerializeField] private int BaseGoldValueMax = 6;

    [Tooltip("Base minimum research value before property and upgrade modifiers are applied.")]
    [SerializeField] private int BaseResearchValueMin = 0;

    [Tooltip("Base maximum research value before property and upgrade modifiers are applied.")]
    [SerializeField] private int BaseResearchValueMax = 1;

    [Header("Properties")]
    [Tooltip("Configurable ore properties such as purity or size.")]
    [SerializeField] private List<OrePropertyRange> PropertyRanges = new();

    /// <summary>
    /// Gets the unique ore identifier.
    /// </summary>
    public string GetOreId()
    {
        return OreId;
    }

    /// <summary>
    /// Gets the display name shown in UI.
    /// </summary>
    public string GetDisplayName()
    {
        return DisplayName;
    }

    /// <summary>
    /// Gets the optional icon used by UI.
    /// </summary>
    public Sprite GetIcon()
    {
        return Icon;
    }

    /// <summary>
    /// Gets the prefab spawned as the mineable ore vein.
    /// </summary>
    public GameObject GetVeinPrefab()
    {
        return VeinPrefab;
    }

    /// <summary>
    /// Gets the prefab spawned as the dropped physical ore piece.
    /// </summary>
    public GameObject GetDroppedOrePrefab()
    {
        return DroppedOrePrefab;
    }

    /// <summary>
    /// Gets the base amount of hits required before the vein breaks.
    /// </summary>
    public int GetBaseHitsRequired()
    {
        return Mathf.Max(1, BaseHitsRequired);
    }

    /// <summary>
    /// Gets the base time required for the vein to fully regrow after being mined.
    /// </summary>
    public float GetBaseRespawnTime()
    {
        return Mathf.Max(0f, BaseRespawnTime);
    }

    /// <summary>
    /// Gets the base minimum amount of ore pieces dropped when the vein breaks.
    /// </summary>
    public int GetBaseDropCountMin()
    {
        return Mathf.Max(0, BaseDropCountMin);
    }

    /// <summary>
    /// Gets the base maximum amount of ore pieces dropped when the vein breaks.
    /// </summary>
    public int GetBaseDropCountMax()
    {
        return Mathf.Max(GetBaseDropCountMin(), BaseDropCountMax);
    }

    /// <summary>
    /// Gets the base weight value in KG before property and upgrade modifiers are applied.
    /// </summary>
    public int GetBaseWeightValue()
    {
        return Mathf.Max(0, BaseWeightValue);
    }

    /// <summary>
    /// Gets the base minimum gold value before property and upgrade modifiers are applied.
    /// </summary>
    public int GetBaseGoldValueMin()
    {
        return Mathf.Max(0, BaseGoldValueMin);
    }

    /// <summary>
    /// Gets the base maximum gold value before property and upgrade modifiers are applied.
    /// </summary>
    public int GetBaseGoldValueMax()
    {
        return Mathf.Max(GetBaseGoldValueMin(), BaseGoldValueMax);
    }

    /// <summary>
    /// Gets the base minimum research value before property and upgrade modifiers are applied.
    /// </summary>
    public int GetBaseResearchValueMin()
    {
        return Mathf.Max(0, BaseResearchValueMin);
    }

    /// <summary>
    /// Gets the base maximum research value before property and upgrade modifiers are applied.
    /// </summary>
    public int GetBaseResearchValueMax()
    {
        return Mathf.Max(GetBaseResearchValueMin(), BaseResearchValueMax);
    }

    /// <summary>
    /// Gets the configured ore property ranges such as purity or size.
    /// </summary>
    public IReadOnlyList<OrePropertyRange> GetPropertyRanges()
    {
        return PropertyRanges;
    }
}