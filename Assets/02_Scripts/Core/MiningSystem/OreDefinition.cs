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

    [Header("Economy")]
    [Tooltip("Base gold value before property and upgrade modifiers are applied.")]
    [SerializeField] private int BaseGoldValue = 10;

    [Tooltip("Base research value before property and upgrade modifiers are applied.")]
    [SerializeField] private int BaseResearchValue = 1;

    [Header("Properties")]
    [Tooltip("Configurable ore properties such as purity or size.")]
    [SerializeField] private List<OrePropertyRange> PropertyRanges = new();

    public string GetOreId() { return OreId; }
    public string GetDisplayName() { return DisplayName; }
    public Sprite GetIcon() { return Icon; }
    public GameObject GetVeinPrefab() { return VeinPrefab; }
    public GameObject GetDroppedOrePrefab() { return DroppedOrePrefab; }
    public int GetBaseHitsRequired() { return Mathf.Max(1, BaseHitsRequired); }
    public float GetBaseRespawnTime() { return Mathf.Max(0f, BaseRespawnTime); }
    public int GetBaseDropCountMin() { return Mathf.Max(0, BaseDropCountMin); }
    public int GetBaseDropCountMax() { return Mathf.Max(GetBaseDropCountMin(), BaseDropCountMax); }
    public int GetBaseGoldValue() { return Mathf.Max(0, BaseGoldValue); }
    public int GetBaseResearchValue() { return Mathf.Max(0, BaseResearchValue); }
    public IReadOnlyList<OrePropertyRange> GetPropertyRanges() { return PropertyRanges; }
}
