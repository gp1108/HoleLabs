using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "OreDefinition_", menuName = "Game/Mining/Ore Definition")]
public sealed class OreDefinition : ScriptableObject
{
    [Serializable]
    public sealed class OrePropertyRange
    {
        [SerializeField] private OrePropertyType PropertyType = OrePropertyType.None;
        [SerializeField] private float MinValue = 0f;
        [SerializeField] private float MaxValue = 1f;
        [SerializeField] private bool AffectedByUpgrades = true;

        public OrePropertyType GetPropertyType() => PropertyType;
        public float GetMinValue() => Mathf.Min(MinValue, MaxValue);
        public float GetMaxValue() => Mathf.Max(MinValue, MaxValue);
        public bool GetAffectedByUpgrades() => AffectedByUpgrades;
    }

    [Header("Identity")]
    [SerializeField] private string OreId;
    [SerializeField] private string DisplayName;
    [SerializeField] private Sprite Icon;

    [Header("World")]
    [SerializeField] private GameObject VeinPrefab;

    [Tooltip("Legacy single dropped ore prefab. Used as fallback when the visual variants list is empty.")]
    [SerializeField] private GameObject DroppedOrePrefab;

    [Tooltip("Optional list of dropped ore visual prefabs. A random one is selected every time a drop is spawned.")]
    [SerializeField] private List<GameObject> DroppedOreVisualPrefabs = new();

    [Header("Mining")]
    [SerializeField] private int BaseHitsRequired = 3;
    [SerializeField] private float BaseRespawnTime = 30f;
    [SerializeField] private int BaseDropCountMin = 1;
    [SerializeField] private int BaseDropCountMax = 2;

    [Header("Weight")]
    [SerializeField] private float BaseWeightValue = 10f;

    [Header("Economy")]
    [SerializeField] private float BaseGoldValueMin = 3f;
    [SerializeField] private float BaseGoldValueMax = 6f;
    [SerializeField] private float BaseResearchValueMin = 0f;
    [SerializeField] private float BaseResearchValueMax = 1f;

    [Header("Properties")]
    [SerializeField] private List<OrePropertyRange> PropertyRanges = new();

    public string GetOreId() => OreId;
    public string GetDisplayName() => DisplayName;
    public Sprite GetIcon() => Icon;
    public GameObject GetVeinPrefab() => VeinPrefab;
    public GameObject GetDroppedOrePrefab() => DroppedOrePrefab;
    public IReadOnlyList<GameObject> GetDroppedOreVisualPrefabs() => DroppedOreVisualPrefabs;
    public int GetBaseHitsRequired() => Mathf.Max(1, BaseHitsRequired);
    public float GetBaseRespawnTime() => Mathf.Max(0f, BaseRespawnTime);
    public int GetBaseDropCountMin() => Mathf.Max(0, BaseDropCountMin);
    public int GetBaseDropCountMax() => Mathf.Max(GetBaseDropCountMin(), BaseDropCountMax);
    public float GetBaseWeightValue() => Mathf.Max(0f, BaseWeightValue);
    public float GetBaseGoldValueMin() => CurrencyMath.RoundCurrency(Mathf.Max(0f, BaseGoldValueMin));
    public float GetBaseGoldValueMax() => CurrencyMath.RoundCurrency(Mathf.Max(GetBaseGoldValueMin(), BaseGoldValueMax));
    public float GetBaseResearchValueMin() => CurrencyMath.RoundCurrency(Mathf.Max(0f, BaseResearchValueMin));
    public float GetBaseResearchValueMax() => CurrencyMath.RoundCurrency(Mathf.Max(GetBaseResearchValueMin(), BaseResearchValueMax));
    public IReadOnlyList<OrePropertyRange> GetPropertyRanges() => PropertyRanges;

    /// <summary>
    /// Returns a random valid dropped ore prefab.
    /// Uses the visual variants list when available and falls back to the legacy single prefab otherwise.
    /// </summary>
    public GameObject GetRandomDroppedOrePrefab()
    {
        if (DroppedOreVisualPrefabs != null && DroppedOreVisualPrefabs.Count > 0)
        {
            List<GameObject> ValidPrefabs = null;

            for (int Index = 0; Index < DroppedOreVisualPrefabs.Count; Index++)
            {
                if (DroppedOreVisualPrefabs[Index] == null)
                {
                    continue;
                }

                if (ValidPrefabs == null)
                {
                    ValidPrefabs = new List<GameObject>();
                }

                ValidPrefabs.Add(DroppedOreVisualPrefabs[Index]);
            }

            if (ValidPrefabs != null && ValidPrefabs.Count > 0)
            {
                int RandomIndex = UnityEngine.Random.Range(0, ValidPrefabs.Count);
                return ValidPrefabs[RandomIndex];
            }
        }

        return DroppedOrePrefab;
    }
}